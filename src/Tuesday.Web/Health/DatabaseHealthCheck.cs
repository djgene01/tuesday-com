using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Web.Health;

/// <summary>
/// Proves the process can open a real connection and execute a statement. A pool that
/// accepts configuration but cannot connect is a failed deployment, not a degraded one.
/// </summary>
public sealed class DatabaseHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
{
    public const string Name = "database";

    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return result is not null && Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1
                ? HealthCheckResult.Healthy("PostgreSQL answered SELECT 1.")
                : HealthCheckResult.Degraded("PostgreSQL returned an unexpected result.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", ex);
        }
    }
}
