using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Collaboration;

public enum NotificationKind
{
    Mention = 1,
    Reply = 2,
    Assigned = 3,
    Automation = 4
}

/// <summary>
/// One thing a person should look at. Notifications are per-user rows, never shared, so
/// reading one cannot affect anyone else — the bell count is a plain COUNT over unread rows.
/// </summary>
public sealed class Notification : ITenantOwned
{
    private Notification()
    {
    }

    private Notification(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? BoardId { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid? UpdateId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string LinkUrl { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public static Notification Create(
        Guid tenantId,
        Guid userId,
        NotificationKind kind,
        string title,
        string body,
        string linkUrl,
        Guid? boardId = null,
        Guid? itemId = null,
        Guid? updateId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkUrl);

        return new Notification(Guid.CreateVersion7())
        {
            TenantId = tenantId,
            UserId = userId,
            Kind = kind,
            Title = title.Length > 300 ? title[..300] : title,
            Body = body.Length > 1000 ? body[..1000] : body,
            LinkUrl = linkUrl.Length > 1000 ? linkUrl[..1000] : linkUrl,
            BoardId = boardId,
            ItemId = itemId,
            UpdateId = updateId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkRead(DateTimeOffset? when = null) => ReadAt ??= when ?? DateTimeOffset.UtcNow;

    public void MarkUnread() => ReadAt = null;
}

/// <summary>Per-person delivery choice. Email is opt-in: silence is the safe default.</summary>
public sealed class NotificationPreference : ITenantOwned
{
    private NotificationPreference()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public bool EmailEnabled { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationPreference Create(Guid tenantId, Guid userId, bool emailEnabled = false) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        UserId = userId,
        EmailEnabled = emailEnabled,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public void SetEmail(bool enabled)
    {
        EmailEnabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
