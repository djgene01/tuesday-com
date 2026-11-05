using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Tenancy;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Tenancy;

/// <summary>
/// Finds the caller's membership before a tenant is known. This is the one place that
/// may bypass the tenant filter, because resolving the tenant is its whole job.
/// </summary>
public sealed class TenantResolver(TuesdayDbContext dbContext)
{
    public async Task<Membership?> FindMembershipAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters is deliberate and confined to this class: without a tenant
        // there is nothing to filter by yet.
        return await dbContext.Memberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(membership => membership.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Looks a membership up by email, used by the sign-in path.</summary>
    public async Task<Membership?> FindMembershipByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var userId = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.NormalizedEmail == email.ToUpperInvariant())
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return userId is null ? null : await FindMembershipAsync(userId.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a user already belongs to a tenant. Also here, and not in a page, because
    /// it needs the same filter bypass for the same reason.
    /// </summary>
    public Task<bool> HasMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Memberships
            .IgnoreQueryFilters()
            .AnyAsync(membership => membership.TenantId == tenantId && membership.UserId == userId, cancellationToken);
}
