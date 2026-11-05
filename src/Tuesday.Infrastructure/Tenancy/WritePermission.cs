using Tuesday.Application.Tenancy;
using Tuesday.Domain.Tenancy;

namespace Tuesday.Infrastructure.Tenancy;

/// <summary>
/// Write permission for board content, enforced in the service layer.
///
/// The UI already disables inputs for read-only roles, but a disabled input is a hint, not a rule:
/// anything that reaches a service method — a circuit message, a future endpoint, a background job —
/// must be refused here as well. Viewers and guests are read-only.
/// </summary>
internal static class WritePermission
{
    internal static bool Allows(ITenantContext tenant) =>
        tenant.Role is TenantRole.Admin or TenantRole.Member;

    internal const string Denied = "This board is read-only for your role.";
}
