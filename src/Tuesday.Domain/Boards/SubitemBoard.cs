using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Boards;

/// <summary>
/// The implicit board that owns a parent board's sub-item columns.
///
/// Sub-item rows stay on the parent board (so one query and one permission check covers both
/// levels), but their column definitions hang off this row. That keeps a sub-item's fields
/// independent of the parent's: adding or renaming a parent column never touches them.
/// </summary>
public sealed class SubitemBoard : ITenantOwned
{
    private SubitemBoard()
    {
    }

    private SubitemBoard(Guid tenantId, Guid boardId, string title)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        Title = title;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>The parent board this sub-item surface belongs to.</summary>
    public Guid BoardId { get; private set; }

    public string Title { get; private set; } = "Subitems";

    public DateTimeOffset CreatedAt { get; private set; }

    public static SubitemBoard Create(Guid tenantId, Guid boardId, string? title = null)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty)
        {
            throw new ArgumentException("A sub-item board needs a tenant and a parent board.");
        }

        var name = string.IsNullOrWhiteSpace(title) ? "Subitems" : title.Trim();

        return new SubitemBoard(tenantId, boardId, name);
    }
}
