using Tuesday.Domain.Tenancy;

namespace Tuesday.Application.Tenancy;

/// <summary>
/// The tenant a request is executing against. Resolved once per request from the
/// authenticated principal, then read by the persistence query filter and by policies.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    Guid? UserId { get; }

    TenantRole? Role { get; }

    bool IsAuthenticated { get; }

    /// <summary>True once both the tenant and the caller's membership are known.</summary>
    bool IsResolved { get; }
}

/// <summary>Mutable resolution surface; only the resolution middleware and tests write it.</summary>
public interface ITenantContextSetter : ITenantContext
{
    void Resolve(Guid tenantId, Guid userId, TenantRole role);

    void Clear();
}

public sealed class TenantContext : ITenantContextSetter
{
    public Guid? TenantId { get; private set; }

    public Guid? UserId { get; private set; }

    public TenantRole? Role { get; private set; }

    public bool IsAuthenticated => UserId is not null;

    public bool IsResolved => TenantId is not null && UserId is not null && Role is not null;

    public void Resolve(Guid tenantId, Guid userId, TenantRole role)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
    }

    public void Clear()
    {
        TenantId = null;
        UserId = null;
        Role = null;
    }
}
