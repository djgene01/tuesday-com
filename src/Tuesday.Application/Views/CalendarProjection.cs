using Tuesday.Application.Boards;

namespace Tuesday.Application.Views;

public sealed record CalendarDay(DateOnly Date, bool InMonth, bool IsToday, IReadOnlyList<ViewCard> Cards);

public sealed record CalendarModel(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CalendarDay> Days,
    IReadOnlyList<ViewCard> Undated)
{
    public int PlacedCount => Days.Sum(day => day.Cards.Count);
}

/// <summary>
/// Places items on the day named by one date column. A month is six whole weeks so the
/// grid never reflows as the month changes; a week is the seven days around an anchor.
/// </summary>
public static class CalendarProjection
{
    public static CalendarModel Month(BoardModel board, Guid dateColumnId, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var start = StartOfWeek(first);
        var end = start.AddDays(41);
        return Build(board, dateColumnId, start, end, first.Month);
    }

    public static CalendarModel Week(BoardModel board, Guid dateColumnId, DateOnly anchor)
    {
        var start = StartOfWeek(anchor);
        var end = start.AddDays(6);
        return Build(board, dateColumnId, start, end, anchor.Month);
    }

    private static CalendarModel Build(BoardModel board, Guid dateColumnId, DateOnly from, DateOnly to, int month)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var placed = new Dictionary<DateOnly, List<ViewCard>>();
        var undated = new List<ViewCard>();

        foreach (var item in board.Items)
        {
            var card = new ViewCard(item.Id, item.Name, item.Cells);
            var date = card.Date(dateColumnId);

            if (date is null)
            {
                undated.Add(card);
                continue;
            }

            if (date < from || date > to)
            {
                continue;
            }

            if (!placed.TryGetValue(date.Value, out var list))
            {
                list = [];
                placed[date.Value] = list;
            }

            list.Add(card);
        }

        var days = new List<CalendarDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add(new CalendarDay(
                date,
                date.Month == month,
                date == today,
                placed.TryGetValue(date, out var cards) ? cards : []));
        }

        return new CalendarModel(from, to, days, undated);
    }

    /// <summary>Monday-based week start; the ISO convention the rest of the plan uses.</summary>
    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
