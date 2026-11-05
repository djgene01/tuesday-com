namespace Tuesday.Web.Http;

/// <summary>
/// Gives an unmatched path a real 404 status while still rendering the styled page.
/// </summary>
/// <remarks>
/// The page itself cannot do this. Assigning a 4xx status inside a Blazor SSR component makes
/// the framework discard the rendered output, so the response arrived as <c>404</c> with zero
/// bytes and browsers substituted their own "this page can't be found" screen. Setting the
/// status here, before the endpoint runs, keeps the body intact. The route pattern is the
/// catch-all on <c>NotFound.razor</c>; API routes are excluded so a client still gets a bare
/// status instead of an HTML page.
/// </remarks>
public sealed class NotFoundStatusMiddleware(RequestDelegate next)
{
    private const string CatchAll = "{*path:nonfile}";
    private const string ExplicitPath = "/not-found";

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsNotFoundRoute(context))
        {
            // Do NOT set the status here: a 4xx status set before rendering makes Blazor
            // discard the body, which is what produced the empty 404. The body is written as
            // a normal render and the status is corrected immediately before headers flush.
            context.Response.OnStarting(() =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsNotFoundRoute(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        // The page is also reachable directly, and a 200 there would be a lie.
        if (context.Request.Path.StartsWithSegments(ExplicitPath))
        {
            return true;
        }

        if (context.GetEndpoint() is RouteEndpoint route)
        {
            var pattern = route.RoutePattern.RawText;

            if (pattern is not null
                && (pattern.Contains(CatchAll, StringComparison.Ordinal)
                    || pattern.Contains("{*path", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        // Fallback: the catch-all also populates the "path" route value.
        return context.Request.RouteValues.ContainsKey("path");
    }
}
