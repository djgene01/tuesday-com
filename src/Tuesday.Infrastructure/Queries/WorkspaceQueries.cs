using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Queries;

public sealed record BoardCard(
    Guid Id,
    string Name,
    string WorkspaceName,
    BoardType Type,
    bool IsGranted,
    bool CanEdit);

/// <summary>
/// Read model for the workspace surface. Visibility rules live here so the page does not
/// re-implement them, and so they can be tested without rendering anything.
/// </summary>
public sealed class WorkspaceQueries(TuesdayDbContext dbContext)
{
    public async Task<IReadOnlyList<BoardCard>> GetVisibleBoardsAsync(
        Guid userId,
        TenantRole role,
        CancellationToken cancellationToken = default)
    {
        var boards = dbContext.Boards.AsNoTracking();
        var workspaces = dbContext.Workspaces.AsNoTracking();
        var grants = dbContext.BoardGrants.AsNoTracking();

        var rows = from board in boards
                   join workspace in workspaces on board.WorkspaceId equals workspace.Id
                   select new { Board = board, Workspace = workspace };

        if (role == TenantRole.Guest)
        {
            var grantedBoardIds = grants.Where(grant => grant.UserId == userId).Select(grant => grant.BoardId);
            rows = rows.Where(row => grantedBoardIds.Contains(row.Board.Id));
        }
        else if (role != TenantRole.Admin)
        {
            // Private boards and private workspaces are invisible unless you own them.
            rows = rows.Where(row =>
                (row.Workspace.Visibility == WorkspaceVisibility.Open
                 && row.Board.Type != BoardType.Private)
                || row.Board.OwnerId == userId
                || row.Workspace.OwnerId == userId);
        }

        var materialised = await rows
            .OrderBy(row => row.Workspace.Name)
            .ThenBy(row => row.Board.Name)
            .Select(row => new
            {
                row.Board.Id,
                row.Board.Name,
                WorkspaceName = row.Workspace.Name,
                row.Board.Type,
                row.Board.OwnerId
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> grantedIds;
        if (role == TenantRole.Guest)
        {
            // The query above already restricted rows to granted boards.
            grantedIds = materialised.Select(row => row.Id).ToHashSet();
        }
        else
        {
            var explicitGrants = await grants
                .Where(grant => grant.UserId == userId)
                .Select(grant => grant.BoardId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            grantedIds = explicitGrants.ToHashSet();
        }

        var canEditByRole = role is TenantRole.Admin or TenantRole.Member;

        return materialised
            .Select(row => new BoardCard(
                row.Id,
                row.Name,
                row.WorkspaceName,
                row.Type,
                grantedIds.Contains(row.Id),
                canEditByRole || row.OwnerId == userId))
            .ToList();
    }

    /// <summary>
    /// Returns null when the caller may not see the board. Callers turn null into 404,
    /// never 403, so an ungranted guest cannot probe for a board's existence.
    /// </summary>
    public async Task<BoardCard?> FindBoardAsync(
        Guid boardId,
        Guid userId,
        TenantRole role,
        CancellationToken cancellationToken = default)
    {
        var row = await (from board in dbContext.Boards.AsNoTracking()
                         join workspace in dbContext.Workspaces.AsNoTracking()
                             on board.WorkspaceId equals workspace.Id
                         where board.Id == boardId
                         select new
                         {
                             board.Id,
                             board.Name,
                             WorkspaceName = workspace.Name,
                             board.Type,
                             board.OwnerId,
                             WorkspaceOwnerId = workspace.OwnerId,
                             WorkspaceVisibility = workspace.Visibility
                         })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var grant = await dbContext.BoardGrants.AsNoTracking()
            .FirstOrDefaultAsync(g => g.BoardId == boardId && g.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var visible = role switch
        {
            TenantRole.Admin => true,
            TenantRole.Guest => grant is not null,
            _ => (row.WorkspaceVisibility == WorkspaceVisibility.Open && row.Type != BoardType.Private)
                 || row.OwnerId == userId
                 || row.WorkspaceOwnerId == userId
        };

        if (!visible)
        {
            return null;
        }

        return new BoardCard(
            row.Id,
            row.Name,
            row.WorkspaceName,
            row.Type,
            grant is not null,
            role is TenantRole.Admin or TenantRole.Member || row.OwnerId == userId);
    }

    public async Task<IReadOnlyList<WorkspaceChoice>> GetWorkspaceChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Workspaces.AsNoTracking()
            .OrderBy(workspace => workspace.Name)
            .Select(workspace => new { workspace.Id, workspace.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => new WorkspaceChoice(row.Id, row.Name)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetWorkspaceNamesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Workspaces.AsNoTracking()
            .OrderBy(workspace => workspace.Name)
            .Select(workspace => workspace.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
