using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Tuesday.Application.Tenancy;
using Tuesday.Infrastructure.Tenancy;

namespace Tuesday.Web.Security;

/// <summary>
/// An interactive circuit gets its own DI scope, and the tenant middleware only runs for
/// HTTP requests. Without this, the first query on a circuit would run unresolved and
/// silently see nothing — or, worse, be widened later. Resolves once per circuit.
/// </summary>
public sealed class TenantContextBootstrapper(
    ITenantContextSetter tenantContext,
    AuthenticationStateProvider authenticationState,
    TenantResolver resolver)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnsureResolvedAsync(CancellationToken cancellationToken = default)
    {
        if (tenantContext.IsResolved)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tenantContext.IsResolved)
            {
                return;
            }

            var state = await authenticationState.GetAuthenticationStateAsync().ConfigureAwait(false);
            var claim = state.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(claim, out var userId))
            {
                return;
            }

            var membership = await resolver.FindMembershipAsync(userId, cancellationToken).ConfigureAwait(false);
            if (membership is not null)
            {
                tenantContext.Resolve(membership.TenantId, membership.UserId, membership.Role);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
