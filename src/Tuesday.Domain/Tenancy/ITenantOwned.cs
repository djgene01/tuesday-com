namespace Tuesday.Domain.Tenancy;

/// <summary>
/// Every row that belongs to a customer carries this. The query filter is applied
/// centrally, so an entity that forgets to implement it fails to compile its filter
/// registration rather than silently leaking across tenants.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
