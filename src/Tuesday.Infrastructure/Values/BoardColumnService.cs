using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Boards;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Values;

public sealed record ColumnAddResult(bool Succeeded, string? Error, BoardColumn? Column)
{
    public static ColumnAddResult Ok(BoardColumn column) => new(true, null, column);

    public static ColumnAddResult Fail(string error) => new(false, error, null);
}

/// <summary>
/// Adds columns with the board limit enforced. The limit lives in the domain, but it can
/// only be checked against the current count, which needs the database.
/// </summary>
public sealed class BoardColumnService(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<ColumnAddResult> AddAsync(
        Guid boardId,
        string title,
        ColumnType type,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var count = await dbContext.BoardColumns
            .CountAsync(column => column.BoardId == boardId, cancellationToken)
            .ConfigureAwait(false);

        if (count >= BoardColumn.MaxColumnsPerBoard)
        {
            return ColumnAddResult.Fail(
                $"A board cannot have more than {BoardColumn.MaxColumnsPerBoard} columns.");
        }

        var key = $"col_{count + 1}";
        var column = BoardColumn.Create(tenantId, boardId, key, title, type, count);

        dbContext.BoardColumns.Add(column);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ColumnAddResult.Ok(column);
    }
}
