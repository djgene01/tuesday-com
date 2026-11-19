namespace Tuesday.Web.Components.Ui;

public enum ButtonVariant
{
    /// <summary>Solid coral: the one main action of a screen or form.</summary>
    Primary,

    /// <summary>Peach fill: a supporting action that still deserves weight.</summary>
    Secondary,

    /// <summary>Bordered white: navigation-like and secondary actions.</summary>
    Ghost,

    /// <summary>No chrome until hovered: row and toolbar actions.</summary>
    Quiet,

    /// <summary>Solid red: confirms a destructive action.</summary>
    Danger,

    /// <summary>Quiet until hovered, then red: the trigger of a destructive action.</summary>
    QuietDanger
}

public enum ButtonSize
{
    /// <summary>28px.</summary>
    Sm,

    /// <summary>32px.</summary>
    Md,

    /// <summary>40px.</summary>
    Lg
}

public enum AlertKind
{
    Error,
    Success,
    Warning,
    Info
}

/// <summary>One link in a <see cref="Breadcrumbs"/> trail. The last crumb has no Href.</summary>
public sealed record Crumb(string Label, string? Href = null);
