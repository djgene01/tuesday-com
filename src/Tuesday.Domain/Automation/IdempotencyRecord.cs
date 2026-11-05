using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Automation;

/// <summary>
/// A stored first response for an idempotent API call. Kept for 24 hours: a retry inside that
/// window replays the original status and body instead of running the side effect twice.
/// </summary>
public sealed class IdempotencyRecord : ITenantOwned
{
    public const int RetentionHours = 24;

    private IdempotencyRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the request body, so the same key with different content is refused.</summary>
    public string RequestHash { get; private set; } = string.Empty;

    public int StatusCode { get; private set; }

    public string ResponseJson { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    public static IdempotencyRecord Create(
        Guid tenantId,
        Guid userId,
        string key,
        string endpoint,
        string requestHash,
        int statusCode,
        string responseJson)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(key));
        }

        var now = DateTimeOffset.UtcNow;

        return new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Key = key,
            Endpoint = endpoint,
            RequestHash = requestHash,
            StatusCode = statusCode,
            ResponseJson = responseJson,
            CreatedAt = now,
            ExpiresAt = now.AddHours(RetentionHours)
        };
    }
}
