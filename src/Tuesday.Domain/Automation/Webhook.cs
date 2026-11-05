using System.Security.Cryptography;
using System.Text;
using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Automation;

/// <summary>
/// One outbound webhook. The secret never leaves the server: it signs the body so the
/// receiver can prove the call came from us, and it is not returned by any read model.
/// </summary>
public sealed class Webhook : ITenantOwned
{
    private Webhook()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string Secret { get; private set; } = string.Empty;

    public bool Enabled { get; private set; } = true;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastCalledAt { get; private set; }

    public int? LastStatus { get; private set; }

    public static Webhook Create(Guid tenantId, Guid boardId, string url, Guid createdBy)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            throw new ArgumentException("A webhook needs an absolute http(s) URL.", nameof(url));
        }

        return new Webhook
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BoardId = boardId,
            Url = uri.ToString(),
            Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static string Sign(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    public void Record(int status)
    {
        LastCalledAt = DateTimeOffset.UtcNow;
        LastStatus = status;
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;
}

/// <summary>
/// A personal API token. Only the hash is stored, and the plaintext is shown to the person
/// once, at creation — the same rule as a password, applied to a bearer string.
/// </summary>
public sealed class ApiToken : ITenantOwned
{
    private ApiToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>First characters, kept for display so a person can tell tokens apart.</summary>
    public string Prefix { get; private set; } = string.Empty;

    public string Hash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static (ApiToken Token, string Plaintext) Issue(Guid tenantId, Guid userId, string name)
    {
        var plaintext = "tue_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var token = new ApiToken
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(name) ? "Token" : name[..Math.Min(name.Length, 100)],
            Prefix = plaintext[..12],
            Hash = HashOf(plaintext),
            CreatedAt = DateTimeOffset.UtcNow
        };

        return (token, plaintext);
    }

    public static string HashOf(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();

    public void Touch() => LastUsedAt = DateTimeOffset.UtcNow;

    public void Revoke() => RevokedAt = DateTimeOffset.UtcNow;
}
