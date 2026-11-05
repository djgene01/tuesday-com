using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Tenancy;

/// <summary>
/// The tenant itself. A tenant is the isolation boundary: one database, one schema,
/// every tenant-owned row filtered by <see cref="TenantId"/>.
/// </summary>
public sealed class Tenant : ITenantOwned
{
    private Tenant()
    {
    }

    private Tenant(string name, string slug)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Slug = slug;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The tenant owns itself, so its own filter is <c>Id == tenant</c>.</summary>
    public Guid TenantId => Id;

    public static Tenant Create(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A tenant needs a name.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("A tenant needs a slug.", nameof(slug));
        }

        return new Tenant(name.Trim(), slug.Trim().ToLowerInvariant());
    }
}
