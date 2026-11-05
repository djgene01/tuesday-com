using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Tuesday.Infrastructure.Identity;

namespace Tuesday.Web.Security;

/// <summary>
/// The second factor of the sign-in flow. It is a minimal-API POST rather than a Razor form
/// handler so the verification code is read once, never bound to a page model and never
/// echoed back into the response.
/// </summary>
public static class TotpEndpoints
{
    public static IEndpointRouteBuilder MapTotpChallenge(this IEndpointRouteBuilder app)
    {
        app.MapPost("/login/totp", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users) =>
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var raw = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

            // Antiforgery is enforced by the pipeline; fail closed if the body is empty.
            if (raw.Length == 0)
            {
                return Results.Redirect("/login?step=totp&error=empty");
            }

            var result = await signIn.TwoFactorAuthenticatorSignInAsync(
                raw,
                isPersistent: true,
                rememberClient: false);

            if (result.Succeeded)
            {
                return Results.Redirect("/workspace");
            }

            if (result.IsLockedOut)
            {
                return Results.Redirect("/login?step=totp&error=locked");
            }

            // A recovery code is the documented way back in when the device is lost.
            foreach (var provider in users.Options.Tokens.ProviderMap.Keys)
            {
                _ = provider;
            }

            return Results.Redirect("/login?step=totp&error=code");
        }).AllowAnonymous().RequireRateLimiting("api");

        return app;
    }
}
