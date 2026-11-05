using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Boards;

/// <summary>
/// The column types implemented so far. The product target is wider; each addition must
/// ship with its own value validation and a cell renderer, so the enum stays honest
/// about what actually works.
/// </summary>
public enum ColumnType
{
    Text = 0,
    LongText = 1,
    Number = 2,
    Status = 3,
    Date = 4,
    Checkbox = 5,
    People = 6,
    Dropdown = 7,
    Dependency = 8,
    Location = 9,
    Country = 10
}

public sealed class BoardColumn : ITenantOwned
{
    /// <summary>The product limit observed on public boards.</summary>
    public const int MaxColumnsPerBoard = 500;

    private BoardColumn()
    {
    }

    private BoardColumn(Guid tenantId, Guid boardId, string key, string title, ColumnType type, int position, string? settingsJson)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        Key = key;
        Title = title;
        Type = type;
        Position = position;
        SettingsJson = settingsJson;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    /// <summary>Stable machine name, unique per board, never shown as the label.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public ColumnType Type { get; private set; }

    public int Position { get; private set; }

    /// <summary>Type-specific options (status labels, dropdown options) as JSON.</summary>
    public string? SettingsJson { get; private set; }

    public static BoardColumn Create(
        Guid tenantId,
        Guid boardId,
        string key,
        string title,
        ColumnType type,
        int position,
        string? settingsJson = null)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty)
        {
            throw new ArgumentException("A column needs a tenant and a board.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A column needs a key.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A column needs a title.", nameof(title));
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return new BoardColumn(tenantId, boardId, key.Trim(), title.Trim(), type, position, settingsJson);
    }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A column needs a title.", nameof(title));
        }

        Title = title.Trim();
    }

    public void MoveTo(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
    }
}
