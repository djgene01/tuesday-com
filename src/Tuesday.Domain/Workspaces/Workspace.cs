using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Workspaces;

public enum WorkspaceVisibility
{
    Open = 0,
    Private = 1
}

/// <summary>
/// A container for boards. Open workspaces are readable by every member, viewer and
/// admin in the tenant; private workspaces are readable only by their owner and admins.
/// </summary>
public sealed class Workspace : ITenantOwned
{
    private Workspace()
    {
    }

    private Workspace(Guid tenantId, string name, WorkspaceVisibility visibility, Guid ownerId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Name = name;
        Visibility = visibility;
        OwnerId = ownerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public WorkspaceVisibility Visibility { get; private set; }

    public Guid OwnerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Workspace Create(
        Guid tenantId,
        string name,
        WorkspaceVisibility visibility,
        Guid ownerId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A workspace needs a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A workspace needs a name.", nameof(name));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A workspace needs an owner.", nameof(ownerId));
        }

        return new Workspace(tenantId, name.Trim(), visibility, ownerId);
    }
}
