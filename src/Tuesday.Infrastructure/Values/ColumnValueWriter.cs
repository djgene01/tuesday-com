using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Items;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Values;

public sealed record ValueWriteResult(bool Succeeded, string? Error)
{
    public static ValueWriteResult Ok() => new(true, null);

    public static ValueWriteResult Fail(string error) => new(false, error);
}

/// <summary>
/// The single write path for every cell and item name. Validation happens here, once, so
/// the grid, the API and any future importer cannot disagree about what a valid value is.
/// Each call takes its own context: an interactive circuit may run two handlers at once,
/// and one scoped DbContext cannot serve both.
/// </summary>
public sealed class ColumnValueWriter(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<ValueWriteResult> SetAsync(
        Guid itemId,
        Guid columnId,
        string? rawValue,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var column = await dbContext.BoardColumns
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == columnId, cancellationToken)
            .ConfigureAwait(false);

        if (column is null)
        {
            return ValueWriteResult.Fail("Column not found.");
        }

        var parsed = Parse(column.Type, rawValue);
        if (!parsed.Succeeded)
        {
            return ValueWriteResult.Fail(parsed.Error!);
        }

        var item = await dbContext.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return ValueWriteResult.Fail("Item not found.");
        }

        if (item.BoardId != column.BoardId)
        {
            return ValueWriteResult.Fail("The column does not belong to this item's board.");
        }

        var cell = await dbContext.ItemColumnValues
            .FirstOrDefaultAsync(
                candidate => candidate.ItemId == itemId && candidate.ColumnId == columnId,
                cancellationToken)
            .ConfigureAwait(false);

        if (cell is null)
        {
            cell = ItemColumnValue.Create(item.TenantId, itemId, columnId);
            dbContext.ItemColumnValues.Add(cell);
        }

        cell.Set(parsed.ValueJson, parsed.TextValue, parsed.NumberValue, parsed.DateValue, parsed.BoolValue);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ValueWriteResult.Ok();
    }

    public async Task<ValueWriteResult> RenameItemAsync(
        Guid itemId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ValueWriteResult.Fail("An item needs a name.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var item = await dbContext.Items
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return ValueWriteResult.Fail("Item not found.");
        }

        item.Rename(name);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ValueWriteResult.Ok();
    }

    public sealed record Parsed(
        bool Succeeded,
        string? Error,
        string? ValueJson,
        string? TextValue,
        decimal? NumberValue,
        DateOnly? DateValue,
        bool? BoolValue);

    private static Parsed Ok(
        string? json,
        string? text = null,
        decimal? number = null,
        DateOnly? date = null,
        bool? flag = null) =>
        new(true, null, json, text, number, date, flag);

    private static Parsed Invalid(string error) => new(false, error, null, null, null, null, null);

    /// <summary>
    /// Parses one raw string into the canonical JSON plus its typed projection. An empty
    /// input clears the cell rather than storing an empty string.
    /// </summary>
    public static Parsed Parse(ColumnType type, string? rawValue)
    {
        var raw = rawValue?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            return Ok(null);
        }

        switch (type)
        {
            case ColumnType.Text:
            case ColumnType.LongText:
                return raw.Length > 2000
                    ? Invalid("Text values are limited to 2000 characters.")
                    : Ok(JsonSerializer.Serialize(raw), raw);

            case ColumnType.Number:
                return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? Ok(JsonSerializer.Serialize(number), number.ToString(CultureInfo.InvariantCulture), number)
                    : Invalid("Enter a number.");

            case ColumnType.Date:
                return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? Ok(JsonSerializer.Serialize(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        date: date)
                    : Invalid("Enter a date as yyyy-MM-dd.");

            case ColumnType.Checkbox:
                if (bool.TryParse(raw, out var flag))
                {
                    return Ok(JsonSerializer.Serialize(flag), flag.ToString(), flag: flag);
                }

                return raw is "1" or "on" or "yes"
                    ? Ok("true", "True", flag: true)
                    : Invalid("Enter true or false.");

            case ColumnType.Status:
            case ColumnType.Dropdown:
                return raw.Length > 200
                    ? Invalid("Choose one of the configured options.")
                    : Ok(JsonSerializer.Serialize(raw), raw);

            case ColumnType.People:
                return Guid.TryParse(raw, out _) || raw.Contains('@', StringComparison.Ordinal)
                    ? Ok(JsonSerializer.Serialize(raw), raw)
                    : Invalid("People values must be a user id or an email address.");

            case ColumnType.Dependency:
                // Stored as the item names this item waits on, comma separated; the graph
                // itself lives in item_dependencies, which is what Gantt reads.
                return raw.Length > 1000
                    ? Invalid("A dependency value cannot exceed 1000 characters.")
                    : Ok(JsonSerializer.Serialize(raw), raw);

            case ColumnType.Location:
                var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return parts.Length is 0 or > 3
                    ? Invalid("A location is latitude, longitude, optionally a place name.")
                    : Ok(JsonSerializer.Serialize(raw), raw);

            case ColumnType.Country:
                return raw.Length == 2 && raw.All(char.IsLetter)
                    ? Ok(JsonSerializer.Serialize(raw.ToUpperInvariant()), raw.ToUpperInvariant())
                    : Invalid("A country is a two-letter ISO code.");

            default:
                return Invalid("Unsupported column type.");
        }
    }
}
