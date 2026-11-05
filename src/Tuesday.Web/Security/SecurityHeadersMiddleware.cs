using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Tuesday.Web.Security;

/// <summary>
/// Response headers the whole app shares, including a nonce-based Content-Security-Policy.
///
/// Scripts are restricted to same-origin files plus the per-request nonce that
/// <c>App.razor</c> puts on the Blazor bootstrap tag. Styles keep <c>'unsafe-inline'</c>
/// on purpose: the grid, timeline and calendar position elements with inline
/// <c>style</c> attributes, and removing that permission would mean moving values the
/// server computes into generated classes.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string NonceItemKey = "csp-nonce";

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Reuse a nonce if this request has already passed through. Blazor can re-run the
        // pipeline for an error or fallback pass, and a second, different nonce in the header
        // would block the bootstrap that the first pass rendered.
        if (context.Items[NonceItemKey] is not string nonce)
        {
            nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceItemKey] = nonce;
        }

        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        headers["Content-Security-Policy"] = string.Join("; ", new[]
        {
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self'",
            "connect-src 'self' ws: wss:",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'self'"
        });

        await next(context).ConfigureAwait(false);
    }
}
