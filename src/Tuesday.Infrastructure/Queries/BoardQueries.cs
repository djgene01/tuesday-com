using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Boards;
using Tuesday.Domain.Boards;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Queries;

/// <summary>
/// Read model for the grid. One round trip per board: columns, groups, items and cells,
/// assembled in memory so the page does not issue a query per row.
/// </summary>
public sealed record ItemPage(IReadOnlyList<ItemView> Items, long? NextSeq, bool HasMore);

public sealed class BoardQueries(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    /// <summary>
    /// Keyset page for the public API, ordered by the database sequence so a cursor stays
    /// valid across reorders and inserts. <paramref name="afterSeq"/> is exclusive.
    /// </summary>
    public async Task<ItemPage> GetItemPageAsync(
        Guid boardId,
        long? afterSeq,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var query = dbContext.Items.AsNoTracking().Where(item => item.BoardId == boardId);

        if (afterSeq is { } after)
        {
            query = query.Where(item => item.Seq > after);
        }

        // One extra row tells us whether another page exists without a count query.
        var rows = await query
            .OrderBy(item => item.Seq)
            .Take(take + 1)
            .Select(item => new { item.Id, item.Name, item.GroupId, item.ParentItemId, item.Seq })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > take;
        var page = hasMore ? rows.Take(take).ToList() : rows;
        var ids = page.Select(row => row.Id).ToList();

        var cells = ids.Count == 0
            ? []
            : await dbContext.ItemColumnValues.AsNoTracking()
                .Where(cell => ids.Contains(cell.ItemId))
                .Select(cell => new { cell.ItemId, cell.ColumnId, cell.TextValue, cell.NumberValue, cell.DateValue, cell.BoolValue })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var byItem = cells
            .GroupBy(cell => cell.ItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<Guid, string?>)group.ToDictionary(
                    cell => cell.ColumnId,
                    cell => Display(cell.TextValue, cell.NumberValue, cell.DateValue, cell.BoolValue)));

        var items = page
            .Select(row => new ItemView(
                row.Id,
                row.Name,
                byItem.TryGetValue(row.Id, out var map) ? map : new Dictionary<Guid, string?>(),
                row.ParentItemId,
                row.GroupId))
            .ToList();

        return new ItemPage(items, page.Count == 0 ? null : page[^1].Seq, hasMore);
    }

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

        // The sub-item surface owns its own column set; it is created lazily by the editor
        // and copied from the parent board on demand, never kept in sync afterwards.
        var subitemBoardId = await dbContext.SubitemBoards.AsNoTracking()
            .Where(candidate => candidate.BoardId == boardId)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var subitemColumns = new List<ColumnView>();

        if (subitemBoardId is { } subitemId)
        {
            var rows = await dbContext.BoardColumns.AsNoTracking()
                .Where(column => column.BoardId == subitemId)
                .OrderBy(column => column.Position)
                .Select(column => new { column.Id, column.Title, column.Type, column.SettingsJson })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            subitemColumns.AddRange(rows.Select(column =>
                new ColumnView(column.Id, column.Title, column.Type, column.SettingsJson)));
        }

        var groups = await dbContext.ItemGroups.AsNoTracking()
            .Where(group => group.BoardId == boardId)
            .OrderBy(group => group.Position)
            .Select(group => new { group.Id, group.Title, group.Colour })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await dbContext.Items.AsNoTracking()
            .Where(item => item.BoardId == boardId)
            .OrderBy(item => item.Position)
            .Select(item => new { item.Id, item.Name, item.GroupId, item.ParentItemId })
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

        // Sub-items are attached to their parent inside its group. A row whose parent lives
        // in another group (only possible if data was written outside the app) is promoted to
        // the top level rather than silently disappearing.
        var byGroup = items.ToLookup(item => item.GroupId);
        var byParent = items.Where(item => item.ParentItemId is not null)
            .ToLookup(item => item.ParentItemId!.Value);
        var groupOf = items.ToDictionary(item => item.Id, item => item.GroupId);

        var groupViews = groups
            .Select(group => new GroupView(
                group.Id,
                group.Title,
                group.Colour,
                byGroup[group.Id]
                    .Where(item => item.ParentItemId is null
                        || !groupOf.TryGetValue(item.ParentItemId.Value, out var parentGroup)
                        || parentGroup != group.Id)
                    .Select(item => new ItemView(
                        item.Id,
                        item.Name,
                        Cells(item.Id),
                        item.ParentItemId,
                        item.GroupId)
                    {
                        SubItems = byParent[item.Id]
                            .Select(child => new ItemView(child.Id, child.Name, Cells(child.Id), child.ParentItemId, child.GroupId))
                            .ToList()
                    })
                    .ToList()))
            .ToList();

        var model = new BoardModel(board.Id, board.Name, columnViews, groupViews)
        {
            SubItemColumns = subitemColumns
        };

        return model;

        IReadOnlyDictionary<Guid, string?> Cells(Guid itemId) =>
            byItem.TryGetValue(itemId, out var map) ? map : new Dictionary<Guid, string?>();
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
