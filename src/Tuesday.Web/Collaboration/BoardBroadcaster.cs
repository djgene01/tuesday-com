using Microsoft.AspNetCore.SignalR;
using Tuesday.Application.Collaboration;

namespace Tuesday.Web.Collaboration;

/// <summary>
/// Fan-out for a finished write. Two audiences, one call: open Blazor circuits subscribed to
/// <see cref="Published"/> refresh their thread in place, and SignalR clients in the board
/// group get the same event without a reload.
/// </summary>
public sealed class BoardBroadcaster(IHubContext<BoardHub> hub) : IUpdateBroadcaster
{
    /// <summary>Raised on the thread that published; subscribers must marshal to their own context.</summary>
    public event Func<UpdateEvent, Task>? Published;

    public async Task PublishAsync(UpdateEvent updateEvent, CancellationToken cancellationToken = default)
    {
        if (Published is { } handlers)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<UpdateEvent, Task>>())
            {
                await handler(updateEvent).ConfigureAwait(false);
            }
        }

        await hub.Clients
            .Group(BoardHub.GroupOf(updateEvent.BoardId))
            .SendAsync("update-changed", updateEvent, cancellationToken)
            .ConfigureAwait(false);
    }
}
