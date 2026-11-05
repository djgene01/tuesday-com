using Microsoft.EntityFrameworkCore;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Collaboration;

public sealed record SearchHit(
    string Kind,
    Guid BoardId,
    string BoardName,
    Guid? ItemId,
    string Title,
    string Snippet);

/// <summary>
/// Search across item names and update bodies, restricted to the boards the caller may open.
/// The board list is computed by the caller from the same permission path the UI uses, so a
/// hit can never point at a board that would answer 404.
/// </summary>
public sealed class SearchService(IDbContextFactory<TuesdayDbContext> contextFactory)
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        IReadOnlyCollection<Guid> visibleBoardIds,
        string? query,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var term = query?.Trim();

        if (string.IsNullOrWhiteSpace(term) || visibleBoardIds.Count == 0)
        {
            return [];
        }

        var pattern = "%" + Escape(term) + "%";

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var boards = await dbContext.Boards.AsNoTracking()
            .Where(board => visibleBoardIds.Contains(board.Id))
            .Select(board => new { board.Id, board.Name })
            .ToDictionaryAsync(board => board.Id, board => board.Name, cancellationToken)
            .ConfigureAwait(false);

        var items = await dbContext.Items.AsNoTracking()
            .Where(item => visibleBoardIds.Contains(item.BoardId) && EF.Functions.Like(item.Name, pattern))
            .OrderBy(item => item.Name)
            .Take(take)
            .Select(item => new { item.Id, item.BoardId, item.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var updates = await dbContext.Updates.AsNoTracking()
            .Where(update => visibleBoardIds.Contains(update.BoardId)
                             && update.DeletedAt == null
                             && EF.Functions.Like(update.BodyText, pattern))
            .OrderByDescending(update => update.CreatedAt)
            .Take(take)
            .Select(update => new { update.Id, update.BoardId, update.ItemId, update.BodyText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hits = new List<SearchHit>();

        foreach (var item in items)
        {
            hits.Add(new SearchHit(
                "item",
                item.BoardId,
                boards.GetValueOrDefault(item.BoardId, "Board"),
                item.Id,
                item.Name,
                $"Item on {boards.GetValueOrDefault(item.BoardId, "a board")}"));
        }

        foreach (var update in updates)
        {
            hits.Add(new SearchHit(
                "update",
                update.BoardId,
                boards.GetValueOrDefault(update.BoardId, "Board"),
                update.ItemId,
                Snippet(update.BodyText, term),
                $"Update on {boards.GetValueOrDefault(update.BoardId, "a board")}"));
        }

        return hits.Take(take).ToList();
    }

    private static string Snippet(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text.Length > 120 ? text[..120] : text;
        }

        var start = Math.Max(0, index - 40);
        var length = Math.Min(text.Length - start, term.Length + 100);
        var snippet = text.Substring(start, length);

        return (start > 0 ? "…" : string.Empty) + snippet + (start + length < text.Length ? "…" : string.Empty);
    }

    /// <summary>LIKE treats these three as wildcards; a user typing them means the literal character.</summary>
    private static string Escape(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
