using System.Security.Cryptography;
using System.Text;

namespace Tuesday.Domain.Tenancy;

/// <summary>
/// A single-use, expiring invitation. Only the hash of the token is stored, so a database leak
/// cannot be replayed as an invitation.
/// </summary>
public sealed class Invite
{
    public const int LifetimeDays = 7;

    private Invite()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public TenantRole Role { get; private set; }

    /// <summary>SHA-256 of the plaintext token, lowercase hex.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedBy { get; private set; }

    public static (Invite Invite, string Token) Create(
        Guid tenantId,
        string email,
        TenantRole role,
        Guid createdBy,
        DateTimeOffset? now = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An invitation needs a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("An invitation needs an email address.", nameof(email));
        }

        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var token = InviteToken.New();

        var invite = new Invite
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            TokenHash = InviteToken.HashOf(token),
            CreatedBy = createdBy,
            CreatedAt = issuedAt,
            ExpiresAt = issuedAt.AddDays(LifetimeDays)
        };

        return (invite, token);
    }

    public bool IsUsable(DateTimeOffset? now = null) =>
        AcceptedAt is null && ExpiresAt > (now ?? DateTimeOffset.UtcNow);

    /// <summary>Marks the invitation spent. Returns false when it was already used or expired.</summary>
    public bool Accept(Guid userId, DateTimeOffset? now = null)
    {
        if (!IsUsable(now))
        {
            return false;
        }

        AcceptedAt = now ?? DateTimeOffset.UtcNow;
        AcceptedBy = userId;
        return true;
    }
}

/// <summary>Token format and hashing, kept pure so it can be tested without a database.</summary>
public static class InviteToken
{
    public const string Prefix = "tue_inv_";

    public static string New()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        return Prefix + Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static bool LooksWellFormed(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.StartsWith(Prefix, StringComparison.Ordinal)
        && token.Length >= Prefix.Length + 32;
}
