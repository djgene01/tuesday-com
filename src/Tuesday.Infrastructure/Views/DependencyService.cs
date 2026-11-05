using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Boards;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Views;

public sealed record DependencyResult(bool Succeeded, string? Error)
{
    public static DependencyResult Ok() => new(true, null);

    public static DependencyResult Fail(string error) => new(false, error);
}

/// <summary>
/// The only writer of the dependency graph. A cycle is refused here: a database cannot
/// express reachability, and a cyclic Gantt is not a rendering bug but a wrong plan.
/// </summary>
public sealed class DependencyService(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<IReadOnlyList<ItemDependency>> ListAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.ItemDependencies.AsNoTracking()
            .Where(dependency => dependency.BoardId == boardId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DependencyResult> AddAsync(
        Guid boardId,
        Guid itemId,
        Guid dependsOnItemId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (itemId == dependsOnItemId)
        {
            return DependencyResult.Fail("An item cannot depend on itself.");
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var onBoard = await dbContext.Items.AsNoTracking()
            .Where(item => item.BoardId == boardId && (item.Id == itemId || item.Id == dependsOnItemId))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!onBoard.Contains(itemId) || !onBoard.Contains(dependsOnItemId))
        {
            return DependencyResult.Fail("Both items must belong to this board.");
        }

        var existing = await dbContext.ItemDependencies.AsNoTracking()
            .Where(dependency => dependency.BoardId == boardId)
            .Select(dependency => new { dependency.ItemId, dependency.DependsOnItemId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Any(edge => edge.ItemId == itemId && edge.DependsOnItemId == dependsOnItemId))
        {
            return DependencyResult.Ok();
        }

        // Walk from the proposed predecessor along its own predecessors. If that walk
        // reaches the successor, the new edge would close a loop.
        var map = existing
            .GroupBy(edge => edge.ItemId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.DependsOnItemId).ToList());

        if (Reaches(map, start: dependsOnItemId, target: itemId))
        {
            return DependencyResult.Fail("That dependency would create a cycle.");
        }

        dbContext.ItemDependencies.Add(ItemDependency.Create(tenantId, boardId, itemId, dependsOnItemId));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DependencyResult.Ok();
    }

    public async Task<bool> RemoveAsync(Guid boardId, Guid itemId, Guid dependsOnItemId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var edge = await dbContext.ItemDependencies
            .FirstOrDefaultAsync(
                dependency => dependency.BoardId == boardId
                    && dependency.ItemId == itemId
                    && dependency.DependsOnItemId == dependsOnItemId,
                cancellationToken)
            .ConfigureAwait(false);

        if (edge is null)
        {
            return false;
        }

        dbContext.ItemDependencies.Remove(edge);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool Reaches(Dictionary<Guid, List<Guid>> map, Guid start, Guid target)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            if (current == target)
            {
                return true;
            }

            if (map.TryGetValue(current, out var next))
            {
                foreach (var candidate in next)
                {
                    stack.Push(candidate);
                }
            }
        }

        return false;
    }
}
