using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tuesday.Infrastructure.Caching;

namespace Tuesday.Web.Health;

public sealed class RedisHealthCheck(RedisConnectionProvider provider) : IHealthCheck
{
    public const string Name = "redis";

    private static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(2);

    private readonly RedisConnectionProvider _provider = provider;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // StackExchange.Redis can spend seconds negotiating a connection it will never
            // get. Readiness must not inherit that; a bounded wait keeps the probe honest.
            var ping = _provider.Multiplexer.GetDatabase().PingAsync();
            var completed = await Task.WhenAny(ping, Task.Delay(ProbeBudget, cancellationToken))
                .ConfigureAwait(false);

            if (completed != ping)
            {
                return HealthCheckResult.Unhealthy($"Redis did not answer within {ProbeBudget.TotalSeconds:F0} s.");
            }

            var latency = await ping.ConfigureAwait(false);
            return HealthCheckResult.Healthy($"Redis answered PING in {latency.TotalMilliseconds:F1} ms.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
