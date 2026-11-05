using Microsoft.AspNetCore.DataProtection;
using System.Globalization;

namespace Tuesday.Web.Collaboration;

public sealed record AttachmentTicket(Guid AttachmentId, Guid BoardId, Guid ItemId, DateTimeOffset ExpiresAt);

/// <summary>
/// Signed, expiring download links. The ticket carries the board and item it was issued for,
/// so a link copied to another item fails even before the permission check runs.
/// </summary>
public sealed class AttachmentLinks(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("tuesday.attachments.v1");

    public string Create(Guid attachmentId, Guid boardId, Guid itemId, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var payload = string.Join('|', attachmentId.ToString("D"), boardId.ToString("D"), itemId.ToString("D"),
            expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        return Uri.EscapeDataString(_protector.Protect(payload));
    }

    public AttachmentTicket? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var payload = _protector.Unprotect(Uri.UnescapeDataString(token));
            var parts = payload.Split('|');

            if (parts.Length != 4
                || !Guid.TryParse(parts[0], out var attachmentId)
                || !Guid.TryParse(parts[1], out var boardId)
                || !Guid.TryParse(parts[2], out var itemId)
                || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresUnix))
            {
                return null;
            }

            var expires = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);

            return expires <= DateTimeOffset.UtcNow
                ? null
                : new AttachmentTicket(attachmentId, boardId, itemId, expires);
        }
        catch (Exception)
        {
            // Tampered or rotated key: treat as no ticket at all.
            return null;
        }
    }
}
