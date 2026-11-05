using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Tuesday.Infrastructure.Caching;

namespace Tuesday.Infrastructure.Automations;

public sealed record QuotaDecision(bool Allowed, int Remaining, int RetryAfterSeconds);

/// <summary>
/// Daily call budget for the public API, counted per bearer token in Redis so every replica
/// sees one counter. A Redis outage fails open: a quota is a commercial control, not a
/// security boundary, and refusing every API call because the cache is down is worse.
/// </summary>
public sealed class ApiQuotaService(
    RedisConnectionProvider redis,
    IConfiguration configuration,
    ILogger<ApiQuotaService> logger)
{
    public const int DefaultDailyLimit = 1000;

    public int DailyLimit =>
        int.TryParse(configuration["Api:DailyQuota"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured)
            ? Math.Max(1, configured)
            : DefaultDailyLimit;

    public async Task<QuotaDecision> ConsumeAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var reset = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var retryAfter = (int)Math.Ceiling((reset - now).TotalSeconds);

        var key = $"api-quota:{now:yyyyMMdd}:{tokenHash}";

        try
        {
            var database = redis.Multiplexer.GetDatabase();
            var used = await database.StringIncrementAsync(key).ConfigureAwait(false);

            if (used == 1)
            {
                await database.KeyExpireAsync(key, TimeSpan.FromSeconds(retryAfter + 60)).ConfigureAwait(false);
            }

            var remaining = Math.Max(0, DailyLimit - (int)used);

            return new QuotaDecision(used <= DailyLimit, remaining, retryAfter);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "API quota could not be counted; allowing the call");
            return new QuotaDecision(true, DailyLimit, retryAfter);
        }
    }
}
