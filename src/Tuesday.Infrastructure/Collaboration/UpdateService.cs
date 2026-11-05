using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Collaboration;
using Tuesday.Domain.Collaboration;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Collaboration;

public sealed record UpdateWriteResult(bool Succeeded, Guid Id, string? Error)
{
    public static UpdateWriteResult Ok(Guid id) => new(true, id, null);

    public static UpdateWriteResult Fail(string error) => new(false, Guid.Empty, error);
}

public sealed record UpdateNode(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    string Html,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool IsDeleted,
    int LikeCount,
    bool LikedByMe,
    IReadOnlyList<UpdateNode> Replies);

/// <summary>
/// The write and read path for item updates. Every post goes through the sanitiser, so
/// there is no route into the database that stores markup the app has not approved.
/// </summary>
public sealed class UpdateService(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    IUpdateBodySanitizer sanitizer,
    IUpdateBroadcaster? broadcaster = null,
    NotificationService? notifications = null)
{
    public async Task<IReadOnlyList<UpdateNode>> ThreadAsync(
        Guid itemId,
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var updates = await dbContext.Updates.AsNoTracking()
            .Where(update => update.ItemId == itemId)
            .OrderBy(update => update.CreatedAt)
            .Select(update => new
            {
                update.Id,
                update.AuthorId,
                update.BodyHtml,
                update.BodyText,
                update.CreatedAt,
                update.EditedAt,
                update.DeletedAt,
                update.ParentId
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (updates.Count == 0)
        {
            return [];
        }

        var ids = updates.Select(update => update.Id).ToList();

        var likes = await dbContext.UpdateLikes.AsNoTracking()
            .Where(like => ids.Contains(like.UpdateId))
            .Select(like => new { like.UpdateId, like.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var authorIds = updates.Select(update => update.AuthorId).Distinct().ToList();
        var names = await dbContext.Users.AsNoTracking()
            .Where(user => authorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName, user.Email })
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName ?? user.Email ?? "unknown", cancellationToken)
            .ConfigureAwait(false);

        var likeCounts = likes
            .GroupBy(like => like.UpdateId)
            .ToDictionary(group => group.Key, group => group.Count());

        var likedByMe = currentUserId is { } me
            ? likes.Where(like => like.UserId == me).Select(like => like.UpdateId).ToHashSet()
            : [];

        UpdateNode Build(
            Guid id,
            Guid authorId,
            string html,
            string text,
            DateTimeOffset createdAt,
            DateTimeOffset? editedAt,
            DateTimeOffset? deletedAt,
            IReadOnlyList<UpdateNode> replies) => new(
            id,
            authorId,
            names.TryGetValue(authorId, out var name) ? name : "unknown",
            html,
            text,
            createdAt,
            editedAt,
            deletedAt is not null,
            likeCounts.GetValueOrDefault(id),
            likedByMe.Contains(id),
            replies);

        var byId = updates.ToDictionary(update => update.Id);

        var roots = new List<UpdateNode>();

        foreach (var update in updates.Where(candidate => candidate.ParentId is null))
        {
            var replies = updates
                .Where(candidate => candidate.ParentId == update.Id)
                .Where(candidate => byId.ContainsKey(candidate.Id))
                .OrderBy(candidate => candidate.CreatedAt)
                .Select(candidate => Build(
                    candidate.Id,
                    candidate.AuthorId,
                    candidate.BodyHtml,
                    candidate.BodyText,
                    candidate.CreatedAt,
                    candidate.EditedAt,
                    candidate.DeletedAt,
                    []))
                .ToList();

            roots.Add(Build(
                update.Id,
                update.AuthorId,
                update.BodyHtml,
                update.BodyText,
                update.CreatedAt,
                update.EditedAt,
                update.DeletedAt,
                replies));
        }

        return roots;
    }

    public Task<UpdateWriteResult> PostAsync(
        Guid boardId,
        Guid itemId,
        Guid authorId,
        string? body,
        CancellationToken cancellationToken = default) =>
        WriteAsync(boardId, itemId, authorId, body, parentId: null, UpdateEventKind.Posted, cancellationToken);

    public async Task<UpdateWriteResult> ReplyAsync(
        Guid threadUpdateId,
        Guid authorId,
        string? body,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var parent = await dbContext.Updates.AsNoTracking()
            .Where(update => update.Id == threadUpdateId)
            .Select(update => new { update.BoardId, update.ItemId, update.ParentId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (parent is null)
        {
            return UpdateWriteResult.Fail("That update does not exist.");
        }

        // A reply to a reply joins the same thread: one level of nesting, always.
        var threadRoot = parent.ParentId ?? threadUpdateId;

        return await WriteAsync(
                parent.BoardId,
                parent.ItemId,
                authorId,
                body,
                threadRoot,
                UpdateEventKind.Replied,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UpdateWriteResult> WriteAsync(
        Guid boardId,
        Guid itemId,
        Guid authorId,
        string? body,
        Guid? parentId,
        UpdateEventKind kind,
        CancellationToken cancellationToken)
    {
        var sanitised = sanitizer.Sanitise(body);

        if (sanitised.IsEmpty)
        {
            return UpdateWriteResult.Fail("An update needs words, not only formatting.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var item = await dbContext.Items.AsNoTracking()
            .Where(candidate => candidate.Id == itemId && candidate.BoardId == boardId)
            .Select(candidate => new { candidate.Id, candidate.TenantId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return UpdateWriteResult.Fail("That item is not on this board.");
        }

        var update = Update.Create(
            item.TenantId,
            boardId,
            itemId,
            authorId,
            sanitised.Html,
            sanitised.Text,
            parentId);

        dbContext.Updates.Add(update);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishAsync(kind, boardId, itemId, update.Id, cancellationToken).ConfigureAwait(false);

        if (notifications is not null && kind is UpdateEventKind.Posted or UpdateEventKind.Replied)
        {
            try
            {
                await notifications
                    .FanOutUpdateAsync(
                        item.TenantId,
                        boardId,
                        itemId,
                        update.Id,
                        authorId,
                        sanitised.Text,
                        kind == UpdateEventKind.Replied,
                        parentId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A notification is a courtesy; the update is already committed and visible.
            }
        }

        return UpdateWriteResult.Ok(update.Id);
    }

    private async Task PublishAsync(
        UpdateEventKind kind,
        Guid boardId,
        Guid itemId,
        Guid updateId,
        CancellationToken cancellationToken)
    {
        if (broadcaster is null)
        {
            return;
        }

        await broadcaster
            .PublishAsync(new UpdateEvent(boardId, itemId, updateId, kind), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Liking twice removes the like; the caller does not need to know which happened.</summary>
    public async Task<bool> ToggleLikeAsync(
        Guid updateId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await dbContext.UpdateLikes
            .FirstOrDefaultAsync(
                like => like.UpdateId == updateId && like.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            dbContext.UpdateLikes.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var removed = await dbContext.Updates.AsNoTracking()
                .Where(candidate => candidate.Id == updateId)
                .Select(candidate => new { candidate.BoardId, candidate.ItemId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (removed is not null)
            {
                await PublishAsync(UpdateEventKind.Liked, removed.BoardId, removed.ItemId, updateId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return false;
        }

        var update = await dbContext.Updates.AsNoTracking()
            .Where(candidate => candidate.Id == updateId)
            .Select(candidate => new { candidate.TenantId, candidate.BoardId, candidate.ItemId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (update is null)
        {
            return false;
        }

        dbContext.UpdateLikes.Add(UpdateLike.Create(update.TenantId, updateId, userId));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishAsync(UpdateEventKind.Liked, update.BoardId, update.ItemId, updateId, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    public async Task<UpdateWriteResult> EditAsync(
        Guid updateId,
        Guid actorId,
        string? body,
        CancellationToken cancellationToken = default)
    {
        var sanitised = sanitizer.Sanitise(body);

        if (sanitised.IsEmpty)
        {
            return UpdateWriteResult.Fail("An update needs words, not only formatting.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var update = await dbContext.Updates
            .FirstOrDefaultAsync(candidate => candidate.Id == updateId, cancellationToken)
            .ConfigureAwait(false);

        if (update is null)
        {
            return UpdateWriteResult.Fail("That update does not exist.");
        }

        if (update.AuthorId != actorId)
        {
            return UpdateWriteResult.Fail("Only the author can edit an update.");
        }

        update.Edit(sanitised.Html, sanitised.Text);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishAsync(UpdateEventKind.Edited, update.BoardId, update.ItemId, update.Id, cancellationToken)
            .ConfigureAwait(false);

        return UpdateWriteResult.Ok(update.Id);
    }

    public async Task<bool> DeleteAsync(
        Guid updateId,
        Guid actorId,
        bool canModerate,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var update = await dbContext.Updates
            .FirstOrDefaultAsync(candidate => candidate.Id == updateId, cancellationToken)
            .ConfigureAwait(false);

        if (update is null || (update.AuthorId != actorId && !canModerate))
        {
            return false;
        }

        update.Delete();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishAsync(UpdateEventKind.Deleted, update.BoardId, update.ItemId, update.Id, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}
