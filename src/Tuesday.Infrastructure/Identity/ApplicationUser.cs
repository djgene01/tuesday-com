using Microsoft.AspNetCore.Identity;

namespace Tuesday.Infrastructure.Identity;

/// <summary>
/// The application user. Identity owns credentials, lockout and TOTP; the tenant
/// relationship lives in <c>Membership</c>, not here, because one user may belong to
/// more than one tenant.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
