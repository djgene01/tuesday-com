using Tuesday.Domain.Boards;

namespace Tuesday.Application.Boards;

public sealed record ColumnView(Guid Id, string Title, ColumnType Type, string? SettingsJson);

/// <summary>
/// One row. <see cref="ParentId"/> is set for a sub-item, and <see cref="SubItems"/> carries
/// the children of a top-level item so the grid can nest without a second query.
/// </summary>
public sealed record ItemView(Guid Id, string Name, IReadOnlyDictionary<Guid, string?> Cells, Guid? ParentId = null, Guid GroupId = default)
{
    public IReadOnlyList<ItemView> SubItems { get; init; } = [];

    public int SubItemCount => SubItems.Count;
}

public sealed record GroupView(Guid Id, string Title, string Colour, IReadOnlyList<ItemView> Items);

/// <summary>
/// One board read into memory: columns, groups and every cell the caller may see. All four
/// views project from this single shape, which is why they cannot disagree about an item.
/// </summary>
public sealed record BoardModel(
    Guid Id,
    string Name,
    IReadOnlyList<ColumnView> Columns,
    IReadOnlyList<GroupView> Groups)
{
    public int ItemCount => Groups.Sum(group => group.Items.Count);

    /// <summary>Every sub-item on the board, across groups.</summary>
    public int SubItemCount => Groups.Sum(group => group.Items.Sum(item => item.SubItems.Count));

    /// <summary>Top-level items only; sub-items are reached through their parent.</summary>
    public IEnumerable<ItemView> Items => Groups.SelectMany(group => group.Items);

    public IEnumerable<ItemView> AllItems => Items.SelectMany(item => item.SubItems.Prepend(item));

    public ColumnView? Column(Guid columnId) => Columns.FirstOrDefault(column => column.Id == columnId);

    public ColumnView? ColumnOfType(ColumnType type) => Columns.FirstOrDefault(column => column.Type == type);
}

/// <summary>
/// The smallest unit every non-grid view draws: an item with the cells that view chose to
/// show. Views never mutate it; a drop hands the change to the value writer instead.
/// </summary>
public sealed record ViewCard(Guid ItemId, string Name, IReadOnlyDictionary<Guid, string?> Cells)
{
    public string? Cell(Guid columnId) =>
        Cells.TryGetValue(columnId, out var value) ? value : null;

    public DateOnly? Date(Guid columnId) =>
        DateOnly.TryParse(Cell(columnId), System.Globalization.CultureInfo.InvariantCulture, out var date)
            ? date
            : null;
}
