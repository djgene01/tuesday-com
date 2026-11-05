using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tuesday.Web.Health;

/// <summary>
/// Writes one JSON object per health request, including every check that ran.
/// The framework default writes only the overall status, which tells an operator
/// nothing about which dependency failed.
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static Task WriteJson(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            correlationId = context.TraceIdentifier,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                error = entry.Value.Exception?.Message
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}
