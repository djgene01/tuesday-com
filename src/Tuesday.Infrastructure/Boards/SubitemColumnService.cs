using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Boards;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Values;

namespace Tuesday.Infrastructure.Boards;

/// <summary>
/// Manages the sub-item surface's own columns: add, rename, retype, reorder and delete.
///
/// These are ordinary column rows under the sub-item board's id, so nothing here touches the
/// parent board. Deleting a column also deletes its values — there is no orphan sweep, so the
/// delete has to be explicit and is confirmed in the UI.
/// </summary>
public sealed class SubitemColumnService(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    ITenantContext tenant)
{
    public async Task<IReadOnlyList<ColumnView>> ListAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var subitemBoardId = await FindAsync(dbContext, boardId, cancellationToken).ConfigureAwait(false);

        if (subitemBoardId is not { } id)
        {
            return [];
        }

        return await dbContext.BoardColumns.AsNoTracking()
            .Where(column => column.BoardId == id)
            .OrderBy(column => column.Position)
            .Select(column => new ColumnView(column.Id, column.Title, column.Type, column.SettingsJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColumnAddResult> AddAsync(
        Guid boardId,
        string title,
        ColumnType type,
        CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return ColumnAddResult.Fail("No tenant is bound to this request.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ColumnAddResult.Fail("A column needs a title.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var subitemBoardId = await SubitemSurface.EnsureAsync(dbContext, tenantId, boardId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await dbContext.BoardColumns
            .Where(column => column.BoardId == subitemBoardId)
            .Select(column => new { column.Key, column.Position })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count >= BoardColumn.MaxColumnsPerBoard)
        {
            return ColumnAddResult.Fail(
                $"A board cannot have more than {BoardColumn.MaxColumnsPerBoard} columns.");
        }

        var keys = existing.Select(column => column.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var key = UniqueKey(title, keys);
        var position = existing.Count == 0 ? 0 : existing.Max(column => column.Position) + 1;

        var column = BoardColumn.Create(tenantId, subitemBoardId, key, title, type, position);

        dbContext.BoardColumns.Add(column);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ColumnAddResult.Ok(column);
    }

    public async Task<ColumnAddResult> RenameAsync(
        Guid columnId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ColumnAddResult.Fail("A column needs a title.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var column = await FindColumnAsync(dbContext, columnId, cancellationToken).ConfigureAwait(false);

        if (column is null)
        {
            return ColumnAddResult.Fail("Column not found.");
        }

        column.Rename(title);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ColumnAddResult.Ok(column);
    }

    /// <summary>Removes the column and every value stored against it.</summary>
    public async Task<ColumnAddResult> DeleteAsync(Guid columnId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var column = await FindColumnAsync(dbContext, columnId, cancellationToken).ConfigureAwait(false);

        if (column is null)
        {
            return ColumnAddResult.Fail("Column not found.");
        }

        await dbContext.ItemColumnValues
            .Where(cell => cell.ColumnId == columnId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.BoardColumns.Remove(column);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ColumnAddResult.Ok(column);
    }

    /// <summary>Moves a column one place left or right, rewriting the sibling positions.</summary>
    public async Task<ColumnAddResult> MoveAsync(
        Guid columnId,
        int delta,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var column = await FindColumnAsync(dbContext, columnId, cancellationToken).ConfigureAwait(false);

        if (column is null)
        {
            return ColumnAddResult.Fail("Column not found.");
        }

        var siblings = await dbContext.BoardColumns
            .Where(candidate => candidate.BoardId == column.BoardId)
            .OrderBy(candidate => candidate.Position)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var index = siblings.FindIndex(candidate => candidate.Id == columnId);
        var target = Math.Clamp(index + delta, 0, siblings.Count - 1);

        if (target == index)
        {
            return ColumnAddResult.Ok(column);
        }

        siblings.RemoveAt(index);
        siblings.Insert(target, column);

        for (var position = 0; position < siblings.Count; position++)
        {
            siblings[position].MoveTo(position);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ColumnAddResult.Ok(column);
    }

    private static async Task<Guid?> FindAsync(
        TuesdayDbContext dbContext,
        Guid boardId,
        CancellationToken cancellationToken) =>
        await dbContext.SubitemBoards.AsNoTracking()
            .Where(candidate => candidate.BoardId == boardId)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<BoardColumn?> FindColumnAsync(
        TuesdayDbContext dbContext,
        Guid columnId,
        CancellationToken cancellationToken)
    {
        // Only columns that live on a sub-item surface are editable here; a parent column id
        // passed in by hand must not be renamed or deleted through this service.
        var subitemBoardId = await dbContext.BoardColumns.AsNoTracking()
            .Where(column => column.Id == columnId)
            .Select(column => column.BoardId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (subitemBoardId == Guid.Empty)
        {
            return null;
        }

        var isSubitemColumn = await dbContext.SubitemBoards.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == subitemBoardId, cancellationToken)
            .ConfigureAwait(false);

        if (!isSubitemColumn)
        {
            return null;
        }

        return await dbContext.BoardColumns
            .FirstOrDefaultAsync(candidate => candidate.Id == columnId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Turns a title into a stable key, suffixed until it is unique on the surface.</summary>
    public static string UniqueKey(string title, IReadOnlySet<string> taken)
    {
        var builder = new StringBuilder();

        foreach (var character in title.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var baseKey = builder.ToString().Trim('_');

        if (baseKey.Length == 0)
        {
            baseKey = "column";
        }

        if (baseKey.Length > 40)
        {
            baseKey = baseKey[..40].Trim('_');
        }

        var key = baseKey;
        var suffix = 2;

        while (taken.Contains(key))
        {
            key = string.Concat(baseKey, "_", suffix.ToString(CultureInfo.InvariantCulture));
            suffix++;
        }

        return key;
    }
}
