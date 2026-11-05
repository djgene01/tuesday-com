using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Views;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Views;

public sealed record ViewSummary(Guid Id, string Name, ViewType Type, int Position, string? SettingsJson);

public sealed record ViewAddResult(bool Succeeded, Guid Id, string? Error)
{
    public static ViewAddResult Ok(Guid id) => new(true, id, null);

    public static ViewAddResult Fail(string error) => new(false, Guid.Empty, error);
}

/// <summary>
/// Creates and lists the projections of a board. The 100-view ceiling is enforced here
/// because it can only be checked against the current count.
/// </summary>
public sealed class ViewService(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<IReadOnlyList<ViewSummary>> ListAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.BoardViews.AsNoTracking()
            .Where(view => view.BoardId == boardId)
            .OrderBy(view => view.Position)
            .Select(view => new ViewSummary(view.Id, view.Name, view.Type, view.Position, view.SettingsJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ViewAddResult> AddAsync(
        Guid boardId,
        Guid tenantId,
        string name,
        ViewType type,
        string? settingsJson = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var count = await dbContext.BoardViews
            .CountAsync(view => view.BoardId == boardId, cancellationToken)
            .ConfigureAwait(false);

        if (count >= BoardView.MaxViewsPerBoard)
        {
            return ViewAddResult.Fail($"A board can hold at most {BoardView.MaxViewsPerBoard} views.");
        }

        var position = await dbContext.BoardViews
            .Where(view => view.BoardId == boardId)
            .Select(view => (int?)view.Position)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? -1;

        var view = BoardView.Create(tenantId, boardId, name, type, position + 1, settingsJson);

        dbContext.BoardViews.Add(view);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ViewAddResult.Ok(view.Id);
    }

    /// <summary>Persists the drag of a view tab itself; the order is what the user chose.</summary>
    public async Task<bool> MoveAsync(Guid viewId, int position, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var view = await dbContext.BoardViews
            .FirstOrDefaultAsync(candidate => candidate.Id == viewId, cancellationToken)
            .ConfigureAwait(false);

        if (view is null)
        {
            return false;
        }

        view.MoveTo(position);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
