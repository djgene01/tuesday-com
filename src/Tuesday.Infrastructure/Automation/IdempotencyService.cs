using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Automation;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Automations;

public enum IdempotencyState
{
    /// <summary>No stored response; the caller should run the operation and then complete the key.</summary>
    New,

    /// <summary>The same key and body already ran; return the stored response.</summary>
    Replay,

    /// <summary>The key exists for a different body, which is a caller bug rather than a retry.</summary>
    Conflict
}

public sealed record IdempotencyOutcome(IdempotencyState State, int StatusCode = 0, string? ResponseJson = null);

/// <summary>
/// First-write-wins replay store for <c>Idempotency-Key</c>. Scoped to the caller, so one
/// person's key cannot observe or block another's response.
/// </summary>
public sealed class IdempotencyService(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public static string HashOf(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    public async Task<IdempotencyOutcome> BeginAsync(
        Guid tenantId,
        Guid userId,
        string key,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.IdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return new IdempotencyOutcome(IdempotencyState.New);
        }

        if (record.IsExpired)
        {
            dbContext.IdempotencyRecords.Remove(record);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new IdempotencyOutcome(IdempotencyState.New);
        }

        if (!string.Equals(record.Endpoint, endpoint, StringComparison.Ordinal)
            || !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyOutcome(IdempotencyState.Conflict);
        }

        // Tenant is re-checked: a token that moved tenants must not replay another tenant's body.
        if (record.TenantId != tenantId)
        {
            return new IdempotencyOutcome(IdempotencyState.Conflict);
        }

        return new IdempotencyOutcome(IdempotencyState.Replay, record.StatusCode, record.ResponseJson);
    }

    public async Task CompleteAsync(
        Guid tenantId,
        Guid userId,
        string key,
        string endpoint,
        string requestHash,
        int statusCode,
        string responseJson,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        dbContext.IdempotencyRecords.Add(IdempotencyRecord.Create(
            tenantId, userId, key, endpoint, requestHash, statusCode, responseJson));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key won the race. Its response is the one that
            // counts; the caller's write has already happened either way, so this is not an error.
        }
    }
}
