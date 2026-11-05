using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Automation;
using Tuesday.Domain.Tenancy;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Automations;

public sealed record ApiTokenRow(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool IsActive);

/// <summary>
/// Bearer tokens for the public API. Lookup is by hash, so a database dump cannot be replayed
/// as credentials, and the plaintext is returned exactly once at issue time.
/// </summary>
public sealed class ApiTokenService(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<(string Plaintext, Guid Id)> IssueAsync(
        Guid tenantId,
        Guid userId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var (token, plaintext) = ApiToken.Issue(tenantId, userId, name);

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        dbContext.ApiTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (plaintext, token.Id);
    }

    public async Task<IReadOnlyList<ApiTokenRow>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.ApiTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.CreatedAt)
            .Select(token => new ApiTokenRow(
                token.Id, token.Name, token.Prefix, token.CreatedAt, token.LastUsedAt, token.RevokedAt == null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var token = await dbContext.ApiTokens
            .FirstOrDefaultAsync(row => row.Id == tokenId && row.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
        {
            return false;
        }

        token.Revoke();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Returns the owner and role for a live token, or null. Never throws on a bad token.</summary>
    public async Task<(Guid UserId, Guid TenantId, TenantRole Role)?> AuthenticateAsync(
        string? plaintext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext) || !plaintext.StartsWith("tue_", StringComparison.Ordinal))
        {
            return null;
        }

        var hash = ApiToken.HashOf(plaintext);

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var token = await dbContext.ApiTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Hash == hash && row.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
        {
            return null;
        }

        var membership = await dbContext.Memberships.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(row => row.TenantId == token.TenantId && row.UserId == token.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (membership is null)
        {
            // A token whose membership was revoked is dead, even before it is revoked here.
            return null;
        }

        token.Touch();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (token.UserId, token.TenantId, membership.Role);
    }
}
