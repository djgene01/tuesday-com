using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tuesday.Application.Collaboration;
using Tuesday.Application.Tenancy;
using Tuesday.Infrastructure.Queries;

namespace Tuesday.Web.Collaboration;

/// <summary>
/// SignalR surface for anything that is not a Blazor circuit — a future mobile client, a
/// dashboard, a bot. Membership of a board group is authorised, never trusted from the client.
/// </summary>
[Authorize]
public sealed class BoardHub(WorkspaceQueries boards, ITenantContext tenant) : Hub
{
    public static string GroupOf(Guid boardId) => $"board:{boardId:D}";

    public async Task<bool> JoinBoard(Guid boardId)
    {
        if (tenant.UserId is not { } userId || tenant.Role is not { } role)
        {
            return false;
        }

        if (await boards.FindBoardAsync(boardId, userId, role).ConfigureAwait(false) is null)
        {
            return false;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(boardId)).ConfigureAwait(false);
        return true;
    }

    public Task LeaveBoard(Guid boardId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(boardId));
}
