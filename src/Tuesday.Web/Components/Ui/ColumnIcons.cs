using Tuesday.Domain.Boards;

namespace Tuesday.Web.Components.Ui;

/// <summary>A quiet type cue for a column, shared by the grid header and the item page.</summary>
public static class ColumnIcons
{
    public static IconName For(ColumnType type) => type switch
    {
        ColumnType.Status => IconName.Status,
        ColumnType.Date => IconName.Calendar,
        ColumnType.Number => IconName.Hash,
        ColumnType.People => IconName.Person,
        ColumnType.Dropdown => IconName.ChevronDown,
        ColumnType.Checkbox => IconName.Check,
        _ => IconName.Text
    };
}
