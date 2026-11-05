using Tuesday.Application.Boards;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Items;

namespace Tuesday.Tests.Unit;

/// <summary>
/// Sub-item rules: one level deep, inheriting the parent's group, and addressable by position.
/// These are the invariants the grid and the reorder service both assume.
/// </summary>
public sealed class ItemHierarchyTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Board = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Group = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Parent = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void A_top_level_item_has_no_parent()
    {
        var item = Item.Create(Tenant, Board, Group, "  Task 001  ", 0);

        Assert.Null(item.ParentItemId);
        Assert.False(item.IsSubItem);
        Assert.Equal("Task 001", item.Name);
        Assert.Equal(0, item.Position);
    }

    [Fact]
    public void A_sub_item_records_its_parent()
    {
        var item = Item.Create(Tenant, Board, Group, "Draft the brief", 2, Parent);

        Assert.Equal(Parent, item.ParentItemId);
        Assert.True(item.IsSubItem);
        Assert.Equal(2, item.Position);
    }

    [Fact]
    public void An_empty_parent_id_is_rejected_rather_than_treated_as_null()
    {
        Assert.Throws<ArgumentException>(() => Item.Create(Tenant, Board, Group, "Orphan", 0, Guid.Empty));
    }

    [Fact]
    public void An_item_needs_a_name()
    {
        Assert.Throws<ArgumentException>(() => Item.Create(Tenant, Board, Group, "   ", 0));
    }

    [Fact]
    public void MoveTo_changes_group_and_position()
    {
        var item = Item.Create(Tenant, Board, Group, "Task 001", 4);
        var target = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        item.MoveTo(target, 1);

        Assert.Equal(target, item.GroupId);
        Assert.Equal(1, item.Position);
    }

    [Fact]
    public void MoveTo_rejects_a_negative_position()
    {
        var item = Item.Create(Tenant, Board, Group, "Task 001", 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.MoveTo(Group, -1));
    }

    [Fact]
    public void A_subitem_board_defaults_its_title_and_needs_a_parent_board()
    {
        var board = SubitemBoard.Create(Tenant, Board);

        Assert.Equal("Subitems", board.Title);
        Assert.Equal(Board, board.BoardId);
        Assert.NotEqual(Guid.Empty, board.Id);

        Assert.Throws<ArgumentException>(() => SubitemBoard.Create(Tenant, Guid.Empty));
    }

    [Fact]
    public void Sub_item_columns_are_separate_from_the_parent_columns()
    {
        var parentColumn = Guid.CreateVersion7();
        var subColumn = Guid.CreateVersion7();

        var board = new BoardModel(
            Board,
            "Campaign calendar",
            [new ColumnView(parentColumn, "Status", ColumnType.Status, null)],
            [])
        {
            SubItemColumns = [new ColumnView(subColumn, "Status", ColumnType.Status, null)]
        };

        Assert.True(board.HasSubItemColumns);
        Assert.Equal(parentColumn, board.ColumnOfType(ColumnType.Status)!.Id);
        Assert.Equal(subColumn, board.SubItemColumnOfType(ColumnType.Status)!.Id);
        Assert.Equal(subColumn, board.Column(subColumn)!.Id);
    }

    [Fact]
    public void A_board_counts_sub_items_separately_from_top_level_items()
    {
        var sub1 = new ItemView(Guid.CreateVersion7(), "Draft", new Dictionary<Guid, string?>());
        var sub2 = new ItemView(Guid.CreateVersion7(), "Review", new Dictionary<Guid, string?>());
        var parent = new ItemView(Guid.CreateVersion7(), "Task 001", new Dictionary<Guid, string?>())
        {
            SubItems = [sub1, sub2]
        };
        var sibling = new ItemView(Guid.CreateVersion7(), "Task 002", new Dictionary<Guid, string?>());

        var board = new BoardModel(
            Board,
            "Campaign calendar",
            [],
            [new GroupView(Group, "This week", "Purple", [parent, sibling])]);

        Assert.Equal(2, board.ItemCount);
        Assert.Equal(2, board.SubItemCount);
        Assert.Equal(4, board.AllItems.Count());
        Assert.Equal(2, parent.SubItemCount);
    }
}
