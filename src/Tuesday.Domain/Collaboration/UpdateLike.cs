using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Collaboration;

/// <summary>
/// One "like" from one person on one update. The unique index on the pair is what makes
/// a double click idempotent rather than a duplicate row.
/// </summary>
public sealed class UpdateLike : ITenantOwned
{
    private UpdateLike()
    {
    }

    private UpdateLike(Guid tenantId, Guid updateId, Guid userId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        UpdateId = updateId;
        UserId = userId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UpdateId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static UpdateLike Create(Guid tenantId, Guid updateId, Guid userId)
    {
        if (tenantId == Guid.Empty || updateId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("A like needs a tenant, an update and a user.");
        }

        return new UpdateLike(tenantId, updateId, userId);
    }
}
