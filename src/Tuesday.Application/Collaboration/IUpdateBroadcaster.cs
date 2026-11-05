namespace Tuesday.Application.Collaboration;

/// <summary>Something happened to a thread that other people may be looking at.</summary>
public sealed record UpdateEvent(Guid BoardId, Guid ItemId, Guid UpdateId, UpdateEventKind Kind);

public enum UpdateEventKind
{
    Posted,
    Replied,
    Liked,
    Edited,
    Deleted
}

/// <summary>
/// One seam between the write path and everyone watching the same item. The Web layer
/// implements it twice — an in-process event that refreshes open Blazor circuits and a
/// SignalR group send for anything else attached to the hub.
/// </summary>
public interface IUpdateBroadcaster
{
    Task PublishAsync(UpdateEvent updateEvent, CancellationToken cancellationToken = default);
}

/// <summary>Used by tests and by any composition that has no fan-out.</summary>
public sealed class NullUpdateBroadcaster : IUpdateBroadcaster
{
    public Task PublishAsync(UpdateEvent updateEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
