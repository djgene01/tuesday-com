using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Views;

/// <summary>
/// A projection of one board. Views never store item data: they store the choice of
/// projection plus its options, so the same rows appear in every one of them.
/// </summary>
public sealed class BoardView : ITenantOwned
{
    /// <summary>The product limit observed on public boards.</summary>
    public const int MaxViewsPerBoard = 100;

    private BoardView()
    {
    }

    private BoardView(Guid tenantId, Guid boardId, string name, ViewType type, int position, string? settingsJson)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        Name = name;
        Type = type;
        Position = position;
        SettingsJson = settingsJson;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ViewType Type { get; private set; }

    public int Position { get; private set; }

    /// <summary>Column choices for this projection, as JSON.</summary>
    public string? SettingsJson { get; private set; }

    public static BoardView Create(
        Guid tenantId,
        Guid boardId,
        string name,
        ViewType type,
        int position,
        string? settingsJson = null)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty)
        {
            throw new ArgumentException("A view needs a tenant and a board.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A view needs a name.", nameof(name));
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return new BoardView(tenantId, boardId, name.Trim(), type, position, settingsJson);
    }

    /// <summary>Records the tab order the user chose.</summary>
    public void MoveTo(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
    }
}

public enum ViewType
{
    Grid = 0,
    Kanban = 1,
    Calendar = 2,
    Timeline = 3,
    Gantt = 4
}
