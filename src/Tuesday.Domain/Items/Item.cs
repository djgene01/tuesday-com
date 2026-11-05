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

    private Item(Guid tenantId, Guid boardId, Guid groupId, string name, int position)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        GroupId = groupId;
        Name = name;
        Position = position;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid GroupId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Item Create(Guid tenantId, Guid boardId, Guid groupId, string name, int position)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty || groupId == Guid.Empty)
        {
            throw new ArgumentException("An item needs a tenant, a board and a group.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An item needs a name.", nameof(name));
        }

        return new Item(tenantId, boardId, groupId, name.Trim(), position);
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
