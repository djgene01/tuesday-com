using Tuesday.Application.Boards;

namespace Tuesday.Application.Views;

public sealed record KanbanLane(string Label, bool IsEmptyLane, IReadOnlyList<ViewCard> Cards);

/// <summary>
/// Groups one board into status lanes. The lane order is the configured label order, so
/// the board reads the same for everyone; a value that is not configured still appears,
/// because hiding real data would be worse than an extra lane.
/// </summary>
public static class KanbanProjection
{
    public const string UnsetLabel = "No status";

    public static IReadOnlyList<KanbanLane> Build(
        BoardModel board,
        Guid statusColumnId,
        Guid? divideByColumnId = null,
        bool hideEmptyLanes = true)
    {
        var statusColumn = board.Column(statusColumnId);
        if (statusColumn is null)
        {
            return [];
        }

        var entries = board.Items
            .Select((item, index) => new
            {
                Card = new ViewCard(
                    item.Id,
                    item.Name,
                    divideByColumnId is { } divideBy
                        ? new Dictionary<Guid, string?> { [divideBy] = Cell(item, divideBy) }
                        : new Dictionary<Guid, string?>()),
                Index = index,
                Status = Cell(item, statusColumnId)
            })
            .ToList();

        var configured = ColumnSettings.Labels(statusColumn.SettingsJson);

        // Configured labels first, then any value the board actually holds that the
        // settings do not mention, then the unset lane.
        var observed = entries
            .Select(entry => entry.Status)
            .Where(status => !string.IsNullOrWhiteSpace(status) && !configured.Contains(status!))
            .Select(status => status!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(status => status, StringComparer.OrdinalIgnoreCase);

        var labels = configured.Concat(observed).ToList();

        var lanes = labels
            .Select(label => new KanbanLane(
                label,
                false,
                entries.Where(entry => string.Equals(entry.Status, label, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Index)
                    .Select(entry => entry.Card)
                    .ToList()))
            .ToList();

        var unset = entries.Where(entry => string.IsNullOrWhiteSpace(entry.Status))
            .OrderBy(entry => entry.Index)
            .Select(entry => entry.Card)
            .ToList();

        if (unset.Count > 0)
        {
            lanes.Add(new KanbanLane(UnsetLabel, true, unset));
        }

        return hideEmptyLanes
            ? lanes.Where(lane => lane.Cards.Count > 0).ToList()
            : lanes;
    }

    private static string? Cell(ItemView item, Guid columnId) =>
        item.Cells.TryGetValue(columnId, out var value) ? value : null;
}
