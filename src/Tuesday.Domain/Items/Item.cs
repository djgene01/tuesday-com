using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Items;

/// <summary>
/// One row on a board. The item name is the first column every board has; every other
/// value lives in <see cref="ItemColumnValue"/>.
/// </summary>
public sealed class Item : ITenantOwned
{
    private Item()
    {
    }

    private Item(Guid tenantId, Guid boardId, Guid groupId, string name, int position, Guid? parentItemId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        GroupId = groupId;
        Name = name;
        Position = position;
        ParentItemId = parentItemId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid GroupId { get; private set; }

    /// <summary>
    /// Set when this row is a sub-item. One level deep on purpose: nesting without a limit
    /// makes the grid's keyboard model and every projection harder to reason about.
    /// </summary>
    public Guid? ParentItemId { get; private set; }

    public bool IsSubItem => ParentItemId is not null;

    public string Name { get; private set; } = string.Empty;

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Item Create(
        Guid tenantId,
        Guid boardId,
        Guid groupId,
        string name,
        int position,
        Guid? parentItemId = null)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty || groupId == Guid.Empty)
        {
            throw new ArgumentException("An item needs a tenant, a board and a group.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An item needs a name.", nameof(name));
        }

        if (parentItemId == Guid.Empty)
        {
            throw new ArgumentException("A parent id must be null or a real item.", nameof(parentItemId));
        }

        return new Item(tenantId, boardId, groupId, name.Trim(), position, parentItemId);
    }

    /// <summary>
    /// Moves the item to a group and position. Reordering is one write per row, which is why
    /// <see cref="Position"/> is an explicit integer rather than a linked list.
    /// </summary>
    public void MoveTo(Guid groupId, int position)
    {
        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A group is required.", nameof(groupId));
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        GroupId = groupId;
        Position = position;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An item needs a name.", nameof(name));
        }

        Name = name.Trim();
    }
}
