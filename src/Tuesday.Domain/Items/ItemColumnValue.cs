using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Items;

/// <summary>
/// One cell. JSON is the canonical value because column types differ too much for one
/// typed column; the typed projections exist only so filters and sorts can use an index.
/// Both are written by one path, so they cannot disagree.
/// </summary>
public sealed class ItemColumnValue : ITenantOwned
{
    private ItemColumnValue()
    {
    }

    private ItemColumnValue(Guid tenantId, Guid itemId, Guid columnId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        ItemId = itemId;
        ColumnId = columnId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid ColumnId { get; private set; }

    public string? ValueJson { get; private set; }

    public string? TextValue { get; private set; }

    public decimal? NumberValue { get; private set; }

    public DateOnly? DateValue { get; private set; }

    public bool? BoolValue { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static ItemColumnValue Create(Guid tenantId, Guid itemId, Guid columnId)
    {
        if (tenantId == Guid.Empty || itemId == Guid.Empty || columnId == Guid.Empty)
        {
            throw new ArgumentException("A cell needs a tenant, an item and a column.");
        }

        return new ItemColumnValue(tenantId, itemId, columnId);
    }

    /// <summary>
    /// The only way to change a cell. Projections are cleared first so a type change
    /// cannot leave a stale indexed value behind.
    /// </summary>
    public void Set(string? valueJson, string? textValue, decimal? numberValue, DateOnly? dateValue, bool? boolValue)
    {
        ValueJson = valueJson;
        TextValue = textValue;
        NumberValue = numberValue;
        DateValue = dateValue;
        BoolValue = boolValue;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
