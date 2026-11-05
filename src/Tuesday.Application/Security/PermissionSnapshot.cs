using Tuesday.Domain.Tenancy;

namespace Tuesday.Application.Security;

/// <summary>
/// Discrete things a caller may do. Policies and UI affordances both read these, so a
/// hidden button and a refused request can never disagree.
/// </summary>
public enum Capability
{
    ViewWorkspace = 0,
    CreateBoard = 1,
    EditBoard = 2,
    ManageMembers = 3,
    ManageTenant = 4,
    UseApi = 5
}

/// <summary>
/// The single source of truth for what a role may do. Every other layer derives from it.
/// </summary>
public sealed class PermissionSnapshot
{
    private static readonly Capability[] AdminCapabilities =
    [
        Capability.ViewWorkspace,
        Capability.CreateBoard,
        Capability.EditBoard,
        Capability.ManageMembers,
        Capability.ManageTenant,
        Capability.UseApi
    ];

    private static readonly Capability[] MemberCapabilities =
    [
        Capability.ViewWorkspace,
        Capability.CreateBoard,
        Capability.EditBoard,
        Capability.UseApi
    ];

    // A viewer reads everything in the tenant and changes nothing. No API token: a
    // read-only human is not the same thing as a read-only integration.
    private static readonly Capability[] ViewerCapabilities =
    [
        Capability.ViewWorkspace
    ];

    // A guest reads only what was granted, never tenant-wide.
    private static readonly Capability[] GuestCapabilities =
    [
        Capability.ViewWorkspace
    ];

    private PermissionSnapshot(TenantRole role, IReadOnlySet<Capability> capabilities)
    {
        Role = role;
        Capabilities = capabilities;
    }

    public TenantRole Role { get; }

    public IReadOnlySet<Capability> Capabilities { get; }

    public static PermissionSnapshot For(TenantRole role) => role switch
    {
        TenantRole.Admin => new PermissionSnapshot(role, AdminCapabilities.ToHashSet()),
        TenantRole.Member => new PermissionSnapshot(role, MemberCapabilities.ToHashSet()),
        TenantRole.Viewer => new PermissionSnapshot(role, ViewerCapabilities.ToHashSet()),
        TenantRole.Guest => new PermissionSnapshot(role, GuestCapabilities.ToHashSet()),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
    };

    public bool Can(Capability capability) => Capabilities.Contains(capability);

    /// <summary>
    /// Guests only see boards they hold a grant for; every other role sees the tenant.
    /// Kept here so the rule is testable without a database.
    /// </summary>
    public bool CanSeeBoard(bool hasGrant) => Role switch
    {
        TenantRole.Admin => true,
        TenantRole.Guest => hasGrant,
        _ => true
    };
}
