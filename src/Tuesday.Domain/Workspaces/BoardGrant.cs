using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Workspaces;

public enum GrantLevel
{
    View = 0,
    Edit = 1,
    Admin = 2
}

/// <summary>
/// An explicit grant of one board to one user. This is the only mechanism that lets a
/// guest read anything, and it is also how a single board can be widened to one member
/// without widening the workspace.
/// </summary>
public sealed class BoardGrant : ITenantOwned
{
    private BoardGrant()
    {
    }

    private BoardGrant(Guid tenantId, Guid boardId, Guid userId, GrantLevel level)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        UserId = userId;
        Level = level;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid UserId { get; private set; }

    public GrantLevel Level { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static BoardGrant Create(Guid tenantId, Guid boardId, Guid userId, GrantLevel level)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A grant needs a tenant.", nameof(tenantId));
        }

        if (boardId == Guid.Empty)
        {
            throw new ArgumentException("A grant needs a board.", nameof(boardId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A grant needs a user.", nameof(userId));
        }

        return new BoardGrant(tenantId, boardId, userId, level);
    }

    public void ChangeLevel(GrantLevel level) => Level = level;
}
