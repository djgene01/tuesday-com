using Tuesday.Application.Boards;
using Tuesday.Domain.Boards;

namespace Tuesday.Application.Views;

public sealed record TimelineBar(
    Guid ItemId,
    string Name,
    DateOnly Start,
    DateOnly End,
    double LeftPercent,
    double WidthPercent)
{
    public int DayCount => End.DayNumber - Start.DayNumber + 1;
}

public sealed record TimelineTick(DateOnly Date, double LeftPercent);

public sealed record TimelineModel(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TimelineBar> Bars,
    IReadOnlyList<TimelineTick> Ticks,
    IReadOnlyList<ViewCard> Unscheduled)
{
    public int DaySpan => To.DayNumber - From.DayNumber + 1;
}

/// <summary>
/// Turns two date columns into proportional bars. Items missing both dates are returned
/// as unscheduled rather than silently dropped, so the view can say what it could not place.
/// </summary>
public static class TimelineProjection
{
    private const int PaddingDays = 3;

    public static TimelineModel Build(BoardModel board, Guid startColumnId, Guid endColumnId)
    {
        var scheduled = new List<(ViewCard Card, DateOnly Start, DateOnly End)>();
        var unscheduled = new List<ViewCard>();

        foreach (var item in board.Items)
        {
            var card = new ViewCard(item.Id, item.Name, item.Cells);
            var start = card.Date(startColumnId);

            if (start is null)
            {
                unscheduled.Add(card);
                continue;
            }

            var end = card.Date(endColumnId) ?? start.Value;
            if (end < start)
            {
                end = start.Value;
            }

            scheduled.Add((card, start.Value, end));
        }

        if (scheduled.Count == 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new TimelineModel(
                today.AddDays(-PaddingDays),
                today.AddDays(PaddingDays),
                [],
                [],
                unscheduled);
        }

        var from = scheduled.Min(entry => entry.Start).AddDays(-PaddingDays);
        var to = scheduled.Max(entry => entry.End).AddDays(PaddingDays);

        var span = to.DayNumber - from.DayNumber + 1;

        var bars = scheduled
            .OrderBy(entry => entry.Start)
            .ThenBy(entry => entry.Card.Name, StringComparer.Ordinal)
            .Select(entry => new TimelineBar(
                entry.Card.ItemId,
                entry.Card.Name,
                entry.Start,
                entry.End,
                Percent(entry.Start.DayNumber - from.DayNumber, span),
                Percent(entry.End.DayNumber - entry.Start.DayNumber + 1, span)))
            .ToList();

        var ticks = new List<TimelineTick>();
        for (var date = from; date <= to; date = date.AddDays(7))
        {
            ticks.Add(new TimelineTick(date, Percent(date.DayNumber - from.DayNumber, span)));
        }

        return new TimelineModel(from, to, bars, ticks, unscheduled);
    }

    public static TimelineBar Shift(TimelineBar bar, int days, DateOnly from, DateOnly to)
    {
        var span = to.DayNumber - from.DayNumber + 1;
        var start = bar.Start.AddDays(days);
        var end = bar.End.AddDays(days);

        return bar with
        {
            Start = start,
            End = end,
            LeftPercent = Percent(start.DayNumber - from.DayNumber, span),
            WidthPercent = Percent(end.DayNumber - start.DayNumber + 1, span)
        };
    }

    private static double Percent(int days, int span) =>
        span <= 0 ? 0 : Math.Round(days * 100.0 / span, 4);
}

public sealed record GanttLink(Guid FromItemId, Guid ToItemId);

public sealed record GanttModel(TimelineModel Timeline, IReadOnlyList<GanttLink> Links);

/// <summary>
/// Gantt is the timeline plus the dependency graph. Only links whose two ends are both
/// drawn become arrows, so a dangling edge cannot produce a line to nowhere.
/// </summary>
public static class GanttProjection
{
    public static GanttModel Build(TimelineModel timeline, IEnumerable<ItemDependency> dependencies)
    {
        var drawn = timeline.Bars.Select(bar => bar.ItemId).ToHashSet();

        var links = dependencies
            .Where(dependency => drawn.Contains(dependency.ItemId) && drawn.Contains(dependency.DependsOnItemId))
            .Select(dependency => new GanttLink(dependency.DependsOnItemId, dependency.ItemId))
            .Distinct()
            .ToList();

        return new GanttModel(timeline, links);
    }
}
