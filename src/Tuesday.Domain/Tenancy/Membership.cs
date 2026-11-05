namespace Tuesday.Domain.Tenancy;

/// <summary>
/// The four roles the product supports. Guests are scoped to explicitly granted
/// boards; every other role is scoped to the tenant.
/// </summary>
public enum TenantRole
{
    Admin = 0,
    Member = 1,
    Viewer = 2,
    Guest = 3
}

public static class TenantRoleExtensions
{
    /// <summary>
    /// Tenants may hold unlimited free viewers and guests, so the ordering of these
    /// values is a permission ordering, not a billing one.
    /// </summary>
    public static bool CanManageTenant(this TenantRole role) => role == TenantRole.Admin;

    public static bool CanCreateContent(this TenantRole role) =>
        role is TenantRole.Admin or TenantRole.Member;

    public static bool CanEditContent(this TenantRole role) =>
        role is TenantRole.Admin or TenantRole.Member;

    public static bool IsTenantWide(this TenantRole role) => role != TenantRole.Guest;
}

public sealed class Membership : ITenantOwned
{
    private Membership()
    {
    }

    private Membership(Guid tenantId, Guid userId, TenantRole role)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public TenantRole Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Membership Create(Guid tenantId, Guid userId, TenantRole role)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A membership needs a tenant.", nameof(tenantId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A membership needs a user.", nameof(userId));
        }

        return new Membership(tenantId, userId, role);
    }

    /// <summary>
    /// Downgrading a viewer to guest revokes their implicit tenant-wide read, so the
    /// caller must also clean up board grants that the new role invalidates.
    /// </summary>
    public void ChangeRole(TenantRole role) => Role = role;
}
