using System.Text.Json;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Tuesday.Infrastructure.Caching;

namespace Tuesday.Web.Collaboration;

/// <summary>
/// Subscribes to the update channel and re-raises each event locally, so a circuit held by this
/// replica refreshes when the write happened on another one. Skips messages this process
/// published, and retries the subscription instead of failing the host when Redis is down.
/// </summary>
public sealed class UpdateBroadcastRelay(
    RedisConnectionProvider redis,
    BoardBroadcaster broadcaster,
    ILogger<UpdateBroadcastRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var subscriber = redis.Multiplexer.GetSubscriber();

                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(BoardBroadcaster.Channel),
                    async (_, value) => await OnMessageAsync(value).ConfigureAwait(false));

                logger.LogInformation("Update relay subscribed to {Channel}.", BoardBroadcaster.Channel);

                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Update relay could not subscribe; retrying in 5 seconds.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task OnMessageAsync(RedisValue value)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<BoardBroadcaster.BroadcastEnvelope>(value.ToString());

            if (envelope is null || envelope.Origin == BoardBroadcaster.InstanceId)
            {
                return;
            }

            await broadcaster.RaiseAsync(envelope.Update).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Discarded an unreadable update message.");
        }
    }
}
