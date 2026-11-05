using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Boards;

/// <summary>
/// One directed edge in the board's dependency graph: <see cref="ItemId"/> waits on
/// <see cref="DependsOnItemId"/>. The graph must stay acyclic, which is enforced by the
/// service that writes it — a database constraint cannot express reachability.
/// </summary>
public sealed class ItemDependency : ITenantOwned
{
    private ItemDependency()
    {
    }

    private ItemDependency(Guid tenantId, Guid boardId, Guid itemId, Guid dependsOnItemId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        ItemId = itemId;
        DependsOnItemId = dependsOnItemId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid DependsOnItemId { get; private set; }

    public static ItemDependency Create(Guid tenantId, Guid boardId, Guid itemId, Guid dependsOnItemId)
    {
        if (itemId == dependsOnItemId)
        {
            throw new ArgumentException("An item cannot depend on itself.", nameof(dependsOnItemId));
        }

        return new ItemDependency(tenantId, boardId, itemId, dependsOnItemId);
    }
}
