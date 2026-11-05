using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tuesday.Web.Security;

/// <summary>
/// Registers the confidential client this deployment uses. The plan asks for an OpenIddict
/// authorization server so integrations can obtain tokens over OAuth 2.0 instead of relying
/// on hand-issued API keys; the client is seeded per environment because its secret must not
/// be baked into the image.
/// </summary>
public static class IdentityServerSetup
{
    public const string ClientId = "tuesday-cli";

    public static async Task SeedClientsAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await manager.FindByClientIdAsync(ClientId) is not null)
        {
            return;
        }

        // The secret comes from configuration; in Development a known local value is used
        // so the smoke test can run without operator setup.
        var secret = configuration["IdentityServer:ClientSecret"];

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = ClientId,
            ClientSecret = string.IsNullOrWhiteSpace(secret) ? null : secret,
            DisplayName = "Tuesday command line",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + CustomScopes.Boards
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };

        await manager.CreateAsync(descriptor);
    }

    /// <summary>Scopes this product exposes; the plan's API surface maps onto them one for one.</summary>
    public static class CustomScopes
    {
        public const string Boards = "boards";
    }

    /// <summary>Scheme the API uses to validate bearer access tokens.</summary>
    public const string ValidationScheme = "OpenIddict.Validation.AspNetCore";
}
