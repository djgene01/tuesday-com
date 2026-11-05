using Microsoft.AspNetCore.Authorization;
using Tuesday.Application.Security;
using Tuesday.Application.Tenancy;

namespace Tuesday.Web.Security;

public sealed record CapabilityRequirement(Capability Capability) : IAuthorizationRequirement;

/// <summary>
/// Grants a capability when the resolved role holds it. Deliberately reads the tenant
/// context rather than cookie claims: a role change then applies to the next request
/// instead of waiting for a re-issued cookie.
/// </summary>
public sealed class CapabilityAuthorizationHandler(ITenantContext tenantContext)
    : AuthorizationHandler<CapabilityRequirement>
{
    private readonly ITenantContext _tenantContext = tenantContext;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CapabilityRequirement requirement)
    {
        if (_tenantContext.Role is { } role
            && PermissionSnapshot.For(role).Can(requirement.Capability))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class CapabilityPolicyExtensions
{
    public static string PolicyName(this Capability capability) => $"capability:{capability}";

    public static AuthorizationPolicyBuilder RequireCapability(
        this AuthorizationPolicyBuilder builder,
        Capability capability) =>
        builder.RequireAuthenticatedUser().AddRequirements(new CapabilityRequirement(capability));

    public static IServiceCollection AddTuesdayAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, CapabilityAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(Capability.ViewWorkspace.PolicyName(), policy => policy.RequireCapability(Capability.ViewWorkspace))
            .AddPolicy(Capability.CreateBoard.PolicyName(), policy => policy.RequireCapability(Capability.CreateBoard))
            .AddPolicy(Capability.EditBoard.PolicyName(), policy => policy.RequireCapability(Capability.EditBoard))
            .AddPolicy(Capability.ManageMembers.PolicyName(), policy => policy.RequireCapability(Capability.ManageMembers))
            .AddPolicy(Capability.ManageTenant.PolicyName(), policy => policy.RequireCapability(Capability.ManageTenant))
            .AddPolicy(Capability.UseApi.PolicyName(), policy => policy.RequireCapability(Capability.UseApi));

        return services;
    }
}
