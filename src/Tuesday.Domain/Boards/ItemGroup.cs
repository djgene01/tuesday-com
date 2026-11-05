using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Boards;

/// <summary>
/// A coloured band of items inside a board. Position is an explicit integer so a reorder
/// is one update rather than a rewrite of the whole list.
/// </summary>
public sealed class ItemGroup : ITenantOwned
{
    private ItemGroup()
    {
    }

    private ItemGroup(Guid tenantId, Guid boardId, string title, string colour, int position)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        Title = title;
        Colour = colour;
        Position = position;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Design-token name, not a raw hex value, so themes can change it.</summary>
    public string Colour { get; private set; } = "slate";

    public int Position { get; private set; }

    public static ItemGroup Create(Guid tenantId, Guid boardId, string title, string colour, int position)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty)
        {
            throw new ArgumentException("A group needs a tenant and a board.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A group needs a title.", nameof(title));
        }

        return new ItemGroup(tenantId, boardId, title.Trim(), colour, position);
    }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A group needs a title.", nameof(title));
        }

        Title = title.Trim();
    }
}
