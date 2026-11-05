using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Boards;
using Tuesday.Domain.Boards;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Queries;

/// <summary>
/// Read model for the grid. One round trip per board: columns, groups, items and cells,
/// assembled in memory so the page does not issue a query per row.
/// </summary>
public sealed class BoardQueries(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<BoardModel?> GetAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var board = await dbContext.Boards.AsNoTracking()
            .Where(candidate => candidate.Id == boardId)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (board is null)
        {
            return null;
        }

        var columns = await dbContext.BoardColumns.AsNoTracking()
            .Where(column => column.BoardId == boardId)
            .OrderBy(column => column.Position)
            .Select(column => new { column.Id, column.Title, column.Type, column.SettingsJson })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var groups = await dbContext.ItemGroups.AsNoTracking()
            .Where(group => group.BoardId == boardId)
            .OrderBy(group => group.Position)
            .Select(group => new { group.Id, group.Title, group.Colour })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await dbContext.Items.AsNoTracking()
            .Where(item => item.BoardId == boardId)
            .OrderBy(item => item.Position)
            .Select(item => new { item.Id, item.Name, item.GroupId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemIds = items.Select(item => item.Id).ToList();

        // Chunked to keep the IN list inside the provider's parameter limits on large boards.
        var cells = new List<(Guid ItemId, Guid ColumnId, string? Text, decimal? Number, DateOnly? Date, bool? Flag)>();
        foreach (var chunk in itemIds.Chunk(2000))
        {
            var rows = await dbContext.ItemColumnValues.AsNoTracking()
                .Where(cell => chunk.Contains(cell.ItemId))
                .Select(cell => new { cell.ItemId, cell.ColumnId, cell.TextValue, cell.NumberValue, cell.DateValue, cell.BoolValue })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            cells.AddRange(rows.Select(row =>
                (row.ItemId, row.ColumnId, row.TextValue, row.NumberValue, row.DateValue, row.BoolValue)));
        }

        var byItem = cells
            .GroupBy(cell => cell.ItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<Guid, string?>)group.ToDictionary(
                    cell => cell.ColumnId,
                    cell => Display(cell.Text, cell.Number, cell.Date, cell.Flag)));

        var columnViews = columns
            .Select(column => new ColumnView(column.Id, column.Title, column.Type, column.SettingsJson))
            .ToList();

        var groupViews = groups
            .Select(group => new GroupView(
                group.Id,
                group.Title,
                group.Colour,
                items.Where(item => item.GroupId == group.Id)
                    .Select(item => new ItemView(
                        item.Id,
                        item.Name,
                        byItem.TryGetValue(item.Id, out var map) ? map : new Dictionary<Guid, string?>()))
                    .ToList()))
            .ToList();

        return new BoardModel(board.Id, board.Name, columnViews, groupViews);
    }

    private static string? Display(string? text, decimal? number, DateOnly? date, bool? flag)
    {
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (number is not null)
        {
            return number.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (date is not null)
        {
            return date.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        return flag switch
        {
            true => "true",
            false => "false",
            _ => null
        };
    }
}
