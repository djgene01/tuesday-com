using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Auditing;

/// <summary>
/// Append-only record of security-relevant actions. Written in the same transaction as
/// the change it describes so an audit row can never describe a change that rolled back.
/// </summary>
public sealed class AuditEntry : ITenantOwned
{
    private AuditEntry()
    {
    }

    private AuditEntry(Guid tenantId, Guid? actorUserId, string action, string entityType, string entityId, string? detail)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        ActorUserId = actorUserId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Detail = detail;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string? Detail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditEntry Record(
        Guid tenantId,
        Guid? actorUserId,
        string action,
        string entityType,
        string entityId,
        string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("An audit entry needs an action.", nameof(action));
        }

        return new AuditEntry(tenantId, actorUserId, action, entityType, entityId, detail);
    }
}
