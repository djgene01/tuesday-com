namespace Tuesday.Web.Security;

/// <summary>
/// Response headers the whole app shares. A script/style CSP is deliberately absent here -
/// it needs its own policy and a report-only soak, tracked as P6-9. (The <c>frame-ancestors</c>
/// policy browsers see comes from Blazor's frame-options middleware, not from this class.)
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        await next(context).ConfigureAwait(false);
    }
}
