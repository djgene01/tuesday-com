using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Workspaces;

/// <summary>
/// Mirrors the three board visibility types users actually see. "Shareable" means the
/// board can be exposed to guests through a grant; "Private" is invisible to everyone
/// except its owner and tenant admins.
/// </summary>
public enum BoardType
{
    Main = 0,
    Shareable = 1,
    Private = 2
}

public sealed class Board : ITenantOwned
{
    private Board()
    {
    }

    private Board(Guid tenantId, Guid workspaceId, string name, BoardType type, Guid ownerId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        WorkspaceId = workspaceId;
        Name = name;
        Type = type;
        OwnerId = ownerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public BoardType Type { get; private set; }

    public Guid OwnerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Board Create(
        Guid tenantId,
        Guid workspaceId,
        string name,
        BoardType type,
        Guid ownerId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A board needs a tenant.", nameof(tenantId));
        }

        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("A board needs a workspace.", nameof(workspaceId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A board needs a name.", nameof(name));
        }

        return new Board(tenantId, workspaceId, name.Trim(), type, ownerId);
    }
}
