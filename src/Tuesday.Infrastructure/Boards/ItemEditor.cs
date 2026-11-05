using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Boards;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Items;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Boards;

/// <summary>
/// Creates rows and moves them between positions and groups.
///
/// A reorder rewrites the sibling positions of the affected group rather than keeping a
/// linked list: one update per row in a small list, and a single ordered read afterwards.
/// </summary>
public sealed class ItemEditor(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    ITenantContext tenant)
{
    /// <summary>Adds a top-level item, or a sub-item when <paramref name="parentItemId"/> is set.</summary>
    public async Task<ItemWriteResult> AddAsync(
        Guid boardId,
        Guid groupId,
        string name,
        Guid? parentItemId = null,
        CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return ItemWriteResult.Fail("No tenant is bound to this request.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ItemWriteResult.Fail("An item needs a name.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var group = await dbContext.ItemGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == groupId && candidate.BoardId == boardId, cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
        {
            return ItemWriteResult.Fail("Group not found.");
        }

        if (parentItemId is { } parentId)
        {
            var parent = await dbContext.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == parentId && candidate.BoardId == boardId, cancellationToken)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return ItemWriteResult.Fail("Parent item not found.");
            }

            if (parent.ParentItemId is not null)
            {
                return ItemWriteResult.Fail("Sub-items cannot have their own sub-items.");
            }

            // A sub-item inherits its parent's group so the two cannot drift apart.
            groupId = parent.GroupId;

            await EnsureSubitemBoardAsync(dbContext, tenantId, boardId, cancellationToken).ConfigureAwait(false);
        }

        var lastPosition = await dbContext.Items
            .Where(item => item.GroupId == groupId && item.ParentItemId == parentItemId)
            .Select(item => (int?)item.Position)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        var item = Item.Create(
            tenantId,
            boardId,
            groupId,
            name,
            (lastPosition ?? -1) + 1,
            parentItemId);

        dbContext.Items.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ItemWriteResult.Ok(item.Id, item.Position);
    }

    /// <summary>
    /// Copies the parent board's item columns into the sub-item board.
    ///
    /// This is a one-way copy by design: a later rename, retype or delete on the parent board
    /// does not propagate, and a sub-item column can be changed without touching the parent.
    /// Running it twice only adds columns whose key is missing.
    /// </summary>
    public async Task<ItemWriteResult> CopyParentColumnsAsync(
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return ItemWriteResult.Fail("No tenant is bound to this request.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var subitemBoardId = await EnsureSubitemBoardAsync(dbContext, tenantId, boardId, cancellationToken)
            .ConfigureAwait(false);

        var parentColumns = await dbContext.BoardColumns.AsNoTracking()
            .Where(column => column.BoardId == boardId)
            .OrderBy(column => column.Position)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingKeys = await dbContext.BoardColumns.AsNoTracking()
            .Where(column => column.BoardId == subitemBoardId)
            .Select(column => column.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var position = existingKeys.Count;
        var copied = 0;

        foreach (var column in parentColumns)
        {
            if (!existing.Add(column.Key))
            {
                continue;
            }

            dbContext.BoardColumns.Add(BoardColumn.Create(
                tenantId,
                subitemBoardId,
                column.Key,
                column.Title,
                column.Type,
                position++,
                column.SettingsJson));

            copied++;
        }

        if (copied > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return ItemWriteResult.Ok(subitemBoardId, copied);
    }

    /// <summary>Returns the sub-item board's id, creating it the first time it is needed.</summary>
    private static async Task<Guid> EnsureSubitemBoardAsync(
        TuesdayDbContext dbContext,
        Guid tenantId,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SubitemBoards
            .Where(candidate => candidate.BoardId == boardId)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is { } id)
        {
            return id;
        }

        var created = SubitemBoard.Create(tenantId, boardId);
        dbContext.SubitemBoards.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    /// <summary>
    /// Moves an item to <paramref name="targetGroupId"/> at <paramref name="targetIndex"/>.
    /// Sub-items follow their parent; a sub-item can only move inside its own group.
    /// </summary>
    public async Task<ItemWriteResult> MoveAsync(
        Guid itemId,
        Guid targetGroupId,
        int targetIndex,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var item = await dbContext.Items
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return ItemWriteResult.Fail("Item not found.");
        }

        var group = await dbContext.ItemGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == targetGroupId && candidate.BoardId == item.BoardId, cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
        {
            return ItemWriteResult.Fail("Target group not found.");
        }

        if (item.ParentItemId is not null && item.GroupId != targetGroupId)
        {
            return ItemWriteResult.Fail("A sub-item stays in its parent's group.");
        }

        var parentId = item.ParentItemId;

        // Siblings, excluding the row being moved (its slot is re-inserted below).
        var siblings = await dbContext.Items
            .Where(candidate => candidate.GroupId == targetGroupId
                && candidate.ParentItemId == parentId
                && candidate.Id != item.Id)
            .OrderBy(candidate => candidate.Position)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var index = Math.Clamp(targetIndex, 0, siblings.Count);
        siblings.Insert(index, item);

        var movedChildren = new List<Item>();
        if (parentId is null && item.GroupId != targetGroupId)
        {
            // A parent carries its sub-items with it, so the group can never disagree.
            movedChildren = await dbContext.Items
                .Where(candidate => candidate.ParentItemId == item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        for (var position = 0; position < siblings.Count; position++)
        {
            var scheduled = siblings[position];
            if (scheduled.GroupId != targetGroupId || scheduled.Position != position)
            {
                scheduled.MoveTo(targetGroupId, position);
            }
        }

        foreach (var child in movedChildren)
        {
            if (child.GroupId != targetGroupId)
            {
                child.MoveTo(targetGroupId, child.Position);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ItemWriteResult.Ok(item.Id, index);
    }
}

public sealed record ItemWriteResult(bool Succeeded, Guid? ItemId, int Position, string? Error)
{
    public static ItemWriteResult Ok(Guid itemId, int position) => new(true, itemId, position, null);

    public static ItemWriteResult Fail(string error) => new(false, null, 0, error);
}
