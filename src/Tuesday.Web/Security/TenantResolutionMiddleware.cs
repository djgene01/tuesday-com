using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Tuesday.Application.Tenancy;
using Tuesday.Infrastructure.Tenancy;

namespace Tuesday.Web.Security;

/// <summary>
/// Resolves the caller's tenant and role once per request. Runs after authentication and
/// before authorization, so policies see a populated context.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextSetter tenantContext,
        TenantResolver resolver)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var membership = await resolver.FindMembershipAsync(userId, context.RequestAborted)
                    .ConfigureAwait(false);

                if (membership is null)
                {
                    // Authenticated but not a member of any tenant: no access anywhere.
                    logger.LogWarning("User {UserId} has no tenant membership.", userId);
                    await context.SignOutAsync().ConfigureAwait(false);
                }
                else
                {
                    tenantContext.Resolve(membership.TenantId, membership.UserId, membership.Role);
                }
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
