using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tuesday.Application.Collaboration;
using Tuesday.Domain.Collaboration;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Collaboration;

public sealed record NotificationRow(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string Body,
    string LinkUrl,
    DateTimeOffset CreatedAt,
    bool IsRead);

/// <summary>
/// Turns a write into notifications for the people who should know: anyone named with
/// <c>@DisplayName</c>, plus everyone already in the thread. The actor is never notified
/// about their own action, and duplicates are collapsed to one row per person per update.
/// </summary>
public sealed class NotificationService(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    IEmailSender emailSender,
    IOptions<EmailOptions>? emailOptions = null,
    ILogger<NotificationService>? logger = null)
{
    public async Task<int> FanOutUpdateAsync(
        Guid tenantId,
        Guid boardId,
        Guid itemId,
        Guid updateId,
        Guid actorId,
        string plainText,
        bool isReply,
        Guid? threadRootId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Display names contain spaces, so a mention is matched against the whole name, not
        // the whitespace-delimited token: "@Mo Member" must find Mo Member, not "Mo".
        var people = await dbContext.Users.AsNoTracking()
            .Select(user => new { user.Id, user.DisplayName, user.Email })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mentioned = people
            .Where(person => !string.IsNullOrWhiteSpace(person.DisplayName)
                             && ContainsMention(plainText, person.DisplayName!))
            .ToList();

        var participantIds = new List<Guid>();

        if (isReply && threadRootId is { } root)
        {
            participantIds = await dbContext.Updates.AsNoTracking()
                .Where(update => update.Id == root || update.ParentId == root)
                .Select(update => update.AuthorId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var participants = participantIds.Count == 0
            ? []
            : await dbContext.Users.AsNoTracking()
                .Where(user => participantIds.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName, user.Email })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var link = $"/board/{boardId:D}/item/{itemId:D}";
        var excerpt = plainText.Length > 160 ? plainText[..160] + "…" : plainText;

        var recipients = new Dictionary<Guid, (NotificationKind Kind, string Name, string? Email)>();

        foreach (var person in participants.Where(person => person.Id != actorId))
        {
            recipients[person.Id] = (NotificationKind.Reply, person.DisplayName ?? "someone", person.Email);
        }

        foreach (var person in mentioned.Where(person => person.Id != actorId))
        {
            recipients[person.Id] = (NotificationKind.Mention, person.DisplayName ?? "someone", person.Email);
        }

        if (recipients.Count == 0)
        {
            return 0;
        }

        var actorName = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorId)
            .Select(user => user.DisplayName ?? "Someone")
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "Someone";

        foreach (var (userId, recipient) in recipients)
        {
            var title = recipient.Kind == NotificationKind.Mention
                ? $"{actorName} mentioned you"
                : $"{actorName} replied";

            dbContext.Notifications.Add(Notification.Create(
                tenantId,
                userId,
                recipient.Kind,
                title,
                excerpt,
                link,
                boardId,
                itemId,
                updateId));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SendEmailsAsync(dbContext, recipients, actorName, excerpt, link, cancellationToken)
            .ConfigureAwait(false);

        return recipients.Count;
    }

    private async Task SendEmailsAsync(
        TuesdayDbContext dbContext,
        Dictionary<Guid, (NotificationKind Kind, string Name, string? Email)> recipients,
        string actorName,
        string excerpt,
        string link,
        CancellationToken cancellationToken)
    {
        if (emailOptions?.Value.Enabled != true)
        {
            return;
        }

        var userIds = recipients.Keys.ToList();

        var optedIn = await dbContext.NotificationPreferences.AsNoTracking()
            .Where(preference => preference.EmailEnabled && userIds.Contains(preference.UserId))
            .Select(preference => preference.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var userId in optedIn)
        {
            var address = recipients[userId].Email;
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            try
            {
                await emailSender.SendAsync(
                        address,
                        $"{actorName} in Tuesday.com",
                        $"<p>{System.Net.WebUtility.HtmlEncode(excerpt)}</p><p><a href=\"{link}\">Open the item</a></p>",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Mail is a courtesy, never a reason for a write to fail.
                logger?.LogWarning(exception, "Notification email to {Address} failed", address);
            }
        }
    }

    /// <summary>True when <c>@Display Name</c> appears as a whole mention, case-insensitively.</summary>
    public static bool ContainsMention(string text, string displayName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var needle = "@" + displayName;
        var index = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var end = index + needle.Length;
            var boundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            var start = index == 0 || char.IsWhiteSpace(text[index - 1]) || text[index - 1] is '(' or '[' or '"';

            if (boundary && start)
            {
                return true;
            }

            index = text.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Plain <c>@Name</c> tokens; kept for callers that only need the raw tokens.</summary>
    public static IReadOnlyList<string> Mentioned(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([' ', '\n', '\r', '\t', ',', '.', ';', ':', '!', '?', '(', ')', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.StartsWith('@') && token.Length > 1)
            .Select(token => token[1..].TrimEnd('.', ',', '!', '?', ')', ':'))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Direct notification, used by automations that know exactly who should hear.</summary>
    public async Task<bool> NotifyAsync(
        Guid tenantId,
        Guid userId,
        NotificationKind kind,
        string title,
        string body,
        string linkUrl,
        Guid? boardId = null,
        Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        dbContext.Notifications.Add(Notification.Create(
            tenantId, userId, kind, title, body, linkUrl, boardId, itemId));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Resolves a display name to a user id, or null when nobody matches.</summary>
    public async Task<Guid?> FindByDisplayNameAsync(string? displayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.Users.AsNoTracking()
            .Where(user => user.DisplayName == displayName)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NotificationRow>> ListAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.Notifications.AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(take)
            .Select(notification => new NotificationRow(
                notification.Id,
                notification.Kind,
                notification.Title,
                notification.Body,
                notification.LinkUrl,
                notification.CreatedAt,
                notification.ReadAt != null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.Notifications.AsNoTracking()
            .CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(row => row.Id == notificationId && row.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            return;
        }

        notification.MarkRead();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var unread = await dbContext.Notifications
            .Where(row => row.UserId == userId && row.ReadAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var notification in unread)
        {
            notification.MarkRead();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetEmailPreferenceAsync(
        Guid tenantId,
        Guid userId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var preference = await dbContext.NotificationPreferences
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (preference is null)
        {
            preference = NotificationPreference.Create(tenantId, userId, enabled);
            dbContext.NotificationPreferences.Add(preference);
        }
        else
        {
            preference.SetEmail(enabled);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return enabled;
    }

    public async Task<bool> EmailEnabledAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.NotificationPreferences.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.EmailEnabled)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
