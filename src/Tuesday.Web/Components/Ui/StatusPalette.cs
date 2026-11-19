namespace Tuesday.Web.Components.Ui;

/// <summary>
/// The one mapping from board data (status labels, group colour names, chart series) to the
/// design tokens. Values are CSS custom properties, never hex, so a token change in
/// Styles/app.css reaches the grid, the dashboard and the views together.
/// </summary>
public static class StatusPalette
{
    private static string Key(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>The solid fill for a status label, as a CSS value.</summary>
    public static string StatusFill(string? label) => Key(label) switch
    {
        "done" => "var(--color-status-done)",
        "stuck" => "var(--color-status-stuck)",
        "working on it" => "var(--color-status-working)",
        "not started" => "var(--color-status-idle)",
        "review" => "var(--color-status-review)",
        _ => "var(--color-status-info)"
    };

    /// <summary>
    /// Utility classes for a status cell or pill. Text is white on the darker fills and ink on
    /// the light ones, because white on the amber or green fill is under 2:1.
    /// </summary>
    public static string StatusClasses(string? value) => Key(value) switch
    {
        "done" => "bg-status-done text-ink",
        "stuck" => "bg-status-stuck text-white",
        "working on it" => "bg-status-working text-ink",
        "not started" => "bg-status-idle text-ink",
        "review" => "bg-status-review text-white",
        "" => "bg-transparent text-ink-soft",
        _ => "bg-status-info/15 text-ink"
    };

    /// <summary>A group's accent, from the colour name stored on the group.</summary>
    public static string GroupFill(string? colour) => Key(colour) switch
    {
        "amber" => "var(--color-status-working)",
        "teal" => "var(--color-status-done)",
        "slate" => "var(--color-status-info)",
        "violet" => "var(--color-status-review)",
        "rose" => "var(--color-status-stuck)",
        _ => "var(--color-status-info)"
    };

    /// <summary>A faint wash of the group accent, so a group band reads without shouting.</summary>
    public static string GroupTint(string? colour) =>
        $"color-mix(in srgb, {GroupFill(colour)} 7%, var(--color-surface))";

    /// <summary>Distinct, token-backed colours for chart series such as "items per group".</summary>
    public static string SeriesFill(int index) => (index % 5) switch
    {
        0 => "var(--color-brand)",
        1 => "var(--color-status-info)",
        2 => "var(--color-status-review)",
        3 => "var(--color-status-done)",
        _ => "var(--color-status-working)"
    };
}
