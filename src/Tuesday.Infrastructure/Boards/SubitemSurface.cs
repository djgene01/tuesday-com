using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Boards;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Boards;

/// <summary>
/// The implicit sub-item surface for a parent board: one row per board, created on demand.
/// Column definitions for sub-items hang off its id, which is what keeps them independent
/// of the parent board's columns.
/// </summary>
public static class SubitemSurface
{
    public static async Task<Guid> EnsureAsync(
        TuesdayDbContext dbContext,
        Guid tenantId,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SubitemBoards
            .Where(candidate => candidate.BoardId == boardId)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is { } id)
        {
            return id;
        }

        var created = SubitemBoard.Create(tenantId, boardId);
        dbContext.SubitemBoards.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created.Id;
    }
}
