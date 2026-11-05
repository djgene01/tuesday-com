using Tuesday.Domain.Boards;

namespace Tuesday.Application.Boards;

public sealed record ColumnView(Guid Id, string Title, ColumnType Type, string? SettingsJson);

public sealed record ItemView(Guid Id, string Name, IReadOnlyDictionary<Guid, string?> Cells);

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

    public IEnumerable<ItemView> Items => Groups.SelectMany(group => group.Items);

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
