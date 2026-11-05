using Tuesday.Application.Boards;
using Tuesday.Application.Views;
using Tuesday.Domain.Boards;

namespace Tuesday.Tests.Unit;

/// <summary>
/// The four views are pure projections of one board read model; these tests pin the rules
/// that a screenshot cannot check — ordering, what is hidden, and how a bar is measured.
/// </summary>
public sealed class ViewProjectionTests
{
    private static readonly Guid StatusColumn = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DateColumn = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EndColumn = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static BoardModel Board(params (string Name, string? Status, string? Start, string? End)[] items)
    {
        var views = items
            .Select((item, index) => new ItemView(
                Guid.CreateVersion7(),
                item.Name,
                new Dictionary<Guid, string?>
                {
                    [StatusColumn] = item.Status,
                    [DateColumn] = item.Start,
                    [EndColumn] = item.End
                }))
            .ToList();

        return new BoardModel(
            Guid.CreateVersion7(),
            "Board",
            [
                new ColumnView(StatusColumn, "Status", ColumnType.Status,
                    """{"labels":["Not started","Working on it","Stuck","Done"]}"""),
                new ColumnView(DateColumn, "Start", ColumnType.Date, null),
                new ColumnView(EndColumn, "End", ColumnType.Date, null)
            ],
            [new GroupView(Guid.CreateVersion7(), "Group", "slate", views)]);
    }

    [Fact]
    public void Kanban_UsesConfiguredLabelOrder_AndHidesEmptyLanes()
    {
        var board = Board(
            ("A", "Done", null, null),
            ("B", "Not started", null, null),
            ("C", "Working on it", null, null));

        var lanes = KanbanProjection.Build(board, StatusColumn, hideEmptyLanes: true);

        Assert.Equal(["Not started", "Working on it", "Done"], lanes.Select(lane => lane.Label));
        Assert.Equal("B", Assert.Single(lanes[0].Cards).Name);
    }

    [Fact]
    public void Kanban_KeepsAnEmptyLane_WhenHidingIsOff()
    {
        var board = Board(("A", "Done", null, null));

        var lanes = KanbanProjection.Build(board, StatusColumn, hideEmptyLanes: false);

        Assert.Equal(4, lanes.Count);
        Assert.Empty(lanes.Single(lane => lane.Label == "Stuck").Cards);
    }

    [Fact]
    public void Kanban_AddsAnUnsetLane_ForItemsWithoutAStatus()
    {
        var board = Board(("A", null, null, null), ("B", "Done", null, null));

        var lanes = KanbanProjection.Build(board, StatusColumn);

        var unset = Assert.Single(lanes, lane => lane.Label == KanbanProjection.UnsetLabel);
        Assert.True(unset.IsEmptyLane);
        Assert.Equal("A", Assert.Single(unset.Cards).Name);
    }

    [Fact]
    public void Kanban_ShowsAnUnconfiguredValue_RatherThanDroppingTheItem()
    {
        var board = Board(("A", "Waiting on legal", null, null));

        var lanes = KanbanProjection.Build(board, StatusColumn);

        Assert.Contains(lanes, lane => lane.Label == "Waiting on legal");
    }

    [Fact]
    public void Calendar_MonthIsSixWholeWeeks_StartingOnMonday()
    {
        var board = Board(("A", "Done", "2026-11-03", "2026-11-05"));

        var month = CalendarProjection.Month(board, DateColumn, 2026, 11);

        Assert.Equal(42, month.Days.Count);
        Assert.Equal(DayOfWeek.Monday, month.Days[0].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 26), month.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 12, 6), month.Days[^1].Date);
    }

    [Fact]
    public void Calendar_PlacesAnItemOnItsDate_AndListsUndatedSeparately()
    {
        var board = Board(
            ("A", "Done", "2026-11-03", null),
            ("B", "Done", null, null));

        var month = CalendarProjection.Month(board, DateColumn, 2026, 11);

        var day = Assert.Single(month.Days, candidate => candidate.Date == new DateOnly(2026, 11, 3));
        Assert.Equal("A", Assert.Single(day.Cards).Name);
        Assert.Equal("B", Assert.Single(month.Undated).Name);
        Assert.True(day.InMonth);
    }

    [Fact]
    public void Calendar_Week_IsSevenDaysAroundTheAnchor()
    {
        var board = Board(("A", "Done", "2026-11-04", null));

        var week = CalendarProjection.Week(board, DateColumn, new DateOnly(2026, 11, 4));

        Assert.Equal(7, week.Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 2), week.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 8), week.Days[^1].Date);
    }

    [Fact]
    public void Timeline_MeasuresBarsAgainstTheWholeSpan()
    {
        var board = Board(
            ("Early", "Done", "2026-11-03", "2026-11-04"),
            ("Late", "Done", "2026-11-13", "2026-11-15"));

        var timeline = TimelineProjection.Build(board, DateColumn, EndColumn);

        Assert.Equal(new DateOnly(2026, 10, 31), timeline.From);
        Assert.Equal(new DateOnly(2026, 11, 18), timeline.To);

        var early = timeline.Bars.Single(bar => bar.Name == "Early");
        var late = timeline.Bars.Single(bar => bar.Name == "Late");

        Assert.Equal(2, early.DayCount);
        Assert.Equal(3, late.DayCount);
        Assert.True(late.LeftPercent > early.LeftPercent);

        // 3 days of 19 — the window starts three days before the first bar.
        Assert.Equal(15.79, early.LeftPercent, precision: 2);
        Assert.InRange(early.LeftPercent + early.WidthPercent, 0, 100);
        Assert.InRange(late.LeftPercent + late.WidthPercent, 0, 100);
    }

    [Fact]
    public void Timeline_TreatsAMissingEndDate_AsASingleDay_AndReportsUnscheduled()
    {
        var board = Board(
            ("Single", "Done", "2026-11-03", null),
            ("No dates", "Done", null, null));

        var timeline = TimelineProjection.Build(board, DateColumn, EndColumn);

        var bar = Assert.Single(timeline.Bars);
        Assert.Equal(1, bar.DayCount);
        Assert.Equal("No dates", Assert.Single(timeline.Unscheduled).Name);
    }

    [Fact]
    public void Timeline_IgnoresAnEndDateBeforeTheStart()
    {
        var board = Board(("Backwards", "Done", "2026-11-10", "2026-11-01"));

        var timeline = TimelineProjection.Build(board, DateColumn, EndColumn);

        var bar = Assert.Single(timeline.Bars);
        Assert.Equal(bar.Start, bar.End);
        Assert.Equal(1, bar.DayCount);
    }

    [Fact]
    public void Gantt_DropsLinksWhoseEndsAreNotDrawn()
    {
        var board = Board(
            ("A", "Done", "2026-11-03", "2026-11-04"),
            ("B", "Done", "2026-11-05", "2026-11-06"));

        var timeline = TimelineProjection.Build(board, DateColumn, EndColumn);
        var a = timeline.Bars.Single(bar => bar.Name == "A").ItemId;
        var b = timeline.Bars.Single(bar => bar.Name == "B").ItemId;

        var tenant = Guid.CreateVersion7();
        var model = GanttProjection.Build(timeline,
        [
            ItemDependency.Create(tenant, Guid.CreateVersion7(), b, a),
            ItemDependency.Create(tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), a)
        ]);

        var link = Assert.Single(model.Links);
        Assert.Equal(a, link.FromItemId);
        Assert.Equal(b, link.ToItemId);
    }

    [Fact]
    public void ViewCeiling_IsOneHundredPerBoard()
    {
        Assert.Equal(100, Tuesday.Domain.Views.BoardView.MaxViewsPerBoard);
    }
}
