using Serilog.Context;

namespace Tuesday.Web.Http;

/// <summary>
/// Establishes one correlation id per request, echoes it to the client and pushes it
/// into the log context so every entry for the request can be found again.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var supplied)
            && !string.IsNullOrWhiteSpace(supplied)
                ? supplied.ToString()
                : Guid.NewGuid().ToString("n");

        context.TraceIdentifier = correlationId;

        if (!context.Response.HasStarted)
        {
            context.Response.Headers[HeaderName] = correlationId;
        }

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
