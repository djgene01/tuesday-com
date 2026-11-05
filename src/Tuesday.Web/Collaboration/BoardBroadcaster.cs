using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Tuesday.Application.Collaboration;
using Tuesday.Infrastructure.Caching;

namespace Tuesday.Web.Collaboration;

/// <summary>
/// Fan-out for a finished write. Three audiences: open Blazor circuits in this process
/// (the <see cref="Published"/> event), SignalR clients in the board group (through the hub,
/// whose Redis backplane reaches other replicas), and circuits in *other* replicas (through the
/// Redis channel this class publishes to and <see cref="UpdateBroadcastRelay"/> reads).
///
/// Redis is best-effort. If it is unreachable the write still succeeds and this replica's own
/// circuits still refresh; only the cross-replica hop is lost, and that is logged.
/// </summary>
public sealed class BoardBroadcaster(
    IHubContext<BoardHub> hub,
    RedisConnectionProvider redis,
    ILogger<BoardBroadcaster> logger) : IUpdateBroadcaster
{
    public const string Channel = "tuesday:updates";

    /// <summary>Identifies this process so a replica does not handle its own message twice.</summary>
    public static string InstanceId { get; } = Guid.NewGuid().ToString("n");

    /// <summary>Raised on the thread that published; subscribers must marshal to their own context.</summary>
    public event Func<UpdateEvent, Task>? Published;

    public async Task PublishAsync(UpdateEvent updateEvent, CancellationToken cancellationToken = default)
    {
        await RaiseAsync(updateEvent).ConfigureAwait(false);

        await hub.Clients
            .Group(BoardHub.GroupOf(updateEvent.BoardId))
            .SendAsync("update-changed", updateEvent, cancellationToken)
            .ConfigureAwait(false);

        await PublishToRedisAsync(updateEvent).ConfigureAwait(false);
    }

    /// <summary>Raises the local event only — used by the relay for a message from another replica.</summary>
    internal async Task RaiseAsync(UpdateEvent updateEvent)
    {
        if (Published is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<UpdateEvent, Task>>())
        {
            await handler(updateEvent).ConfigureAwait(false);
        }
    }

    private async Task PublishToRedisAsync(UpdateEvent updateEvent)
    {
        try
        {
            var envelope = JsonSerializer.Serialize(new BroadcastEnvelope(InstanceId, updateEvent));

            await redis.Multiplexer
                .GetSubscriber()
                .PublishAsync(RedisChannel.Literal(Channel), envelope)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis fan-out failed; the update refreshed this replica only.");
        }
    }

    internal sealed record BroadcastEnvelope(string Origin, UpdateEvent Update);
}
