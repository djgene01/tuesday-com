using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Tuesday.Infrastructure.Storage;

namespace Tuesday.Web.Health;

/// <summary>
/// Attachments fail silently when the volume is mounted read-only or missing, so this
/// probe writes and removes a small file rather than only checking for the directory.
/// </summary>
public sealed class StorageHealthCheck(IOptions<StorageOptions> options) : IHealthCheck
{
    public const string Name = "storage";

    private readonly StorageOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Root))
        {
            return HealthCheckResult.Unhealthy("Storage:Root is not configured.");
        }

        try
        {
            Directory.CreateDirectory(_options.Root);
            var probe = Path.Combine(_options.Root, $".probe-{Guid.NewGuid():n}");
            await File.WriteAllTextAsync(probe, "ok", cancellationToken).ConfigureAwait(false);
            File.Delete(probe);

            return HealthCheckResult.Healthy($"Storage root '{_options.Root}' is writable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Storage root is not writable.", ex);
        }
    }
}
