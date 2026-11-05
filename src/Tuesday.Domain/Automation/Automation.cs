using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Automation;

public enum AutomationTrigger
{
    /// <summary>A column value on an item changed.</summary>
    CellChanged = 1,

    /// <summary>The item's date column has passed and it is still not done.</summary>
    DatePassed = 2,

    /// <summary>An update was posted on the item.</summary>
    UpdatePosted = 3
}

public enum AutomationAction
{
    SetStatus = 1,
    SetDate = 2,
    NotifyOwner = 3,
    PostUpdate = 4,
    CallWebhook = 5
}

/// <summary>
/// Trigger → condition → action, stored as data. Conditions are deliberately a small,
/// closed grammar (<c>key=value</c>, <c>key!=value</c>, <c>contains:text</c>) rather than a
/// scripting surface: an automation that can run arbitrary code is a remote shell with a
/// friendly name.
/// </summary>
public sealed class Automation : ITenantOwned
{
    public const int MaxPerBoard = 200;

    /// <summary>A rule is a short ordered list of actions, not a workflow engine.</summary>
    public const int MaxSteps = 10;

    /// <summary>
    /// A chain may rewrite values that fire another rule. Three hops is the documented ceiling:
    /// deeper chains are almost always a mistake, and an unbounded one is an outage.
    /// </summary>
    public const int MaxChainDepth = 3;

    private Automation()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public AutomationTrigger Trigger { get; private set; }

    /// <summary>Empty means "always".</summary>
    public string Condition { get; private set; } = string.Empty;

    public AutomationAction Action { get; private set; }

    /// <summary>Action payload: the column key for SetStatus/SetDate, the text for the rest.</summary>
    public string ActionValue { get; private set; } = string.Empty;

    public bool Enabled { get; private set; } = true;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Ordered actions. Position starts at 0 and is dense.</summary>
    public List<AutomationStep> Steps { get; } = [];

    public static Automation Create(
        Guid tenantId,
        Guid boardId,
        string name,
        AutomationTrigger trigger,
        string? condition,
        AutomationAction action,
        string? actionValue,
        Guid createdBy) =>
        Create(tenantId, boardId, name, trigger, condition, [(action, actionValue ?? string.Empty)], createdBy);

    public static Automation Create(
        Guid tenantId,
        Guid boardId,
        string name,
        AutomationTrigger trigger,
        string? condition,
        IReadOnlyList<(AutomationAction Action, string Value)> steps,
        Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (steps.Count is 0)
        {
            throw new ArgumentException("An automation needs at least one action.", nameof(steps));
        }

        if (steps.Count > MaxSteps)
        {
            throw new ArgumentException($"An automation can run at most {MaxSteps} actions.", nameof(steps));
        }

        if (steps.Any(step => string.IsNullOrWhiteSpace(step.Value)))
        {
            throw new ArgumentException("Every action needs a value.", nameof(steps));
        }

        var automation = new Automation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BoardId = boardId,
            Name = name.Length > 200 ? name[..200] : name,
            Trigger = trigger,
            Condition = condition?.Trim() ?? string.Empty,
            Action = steps[0].Action,
            ActionValue = steps[0].Value,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };

        for (var position = 0; position < steps.Count; position++)
        {
            automation.Steps.Add(AutomationStep.Create(
                tenantId, automation.Id, position, steps[position].Action, steps[position].Value));
        }

        return automation;
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    /// <summary>
    /// Parses the closed condition grammar. A malformed condition never matches, so a typo
    /// cannot silently fire an automation on every item.
    /// </summary>
    public bool Matches(IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(Condition))
        {
            return true;
        }

        var condition = Condition.Trim();

        if (condition.StartsWith("contains:", StringComparison.OrdinalIgnoreCase))
        {
            var needle = condition["contains:".Length..];
            return values.Values.Any(value =>
                value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var separator = condition.Contains("!=", StringComparison.Ordinal)
            ? "!="
            : condition.Contains('=', StringComparison.Ordinal) ? "=" : null;

        if (separator is null)
        {
            return false;
        }

        var parts = condition.Split(separator, 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var key = parts[0].Trim();
        var expected = parts[1].Trim();

        if (!values.TryGetValue(key, out var actual))
        {
            return false;
        }

        var equal = string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        return separator == "=" ? equal : !equal;
    }
}


/// <summary>One ordered action inside an automation.</summary>
public sealed class AutomationStep : ITenantOwned
{
    private AutomationStep()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid AutomationId { get; private set; }

    public int Position { get; private set; }

    public AutomationAction Action { get; private set; }

    public string ActionValue { get; private set; } = string.Empty;

    internal static AutomationStep Create(
        Guid tenantId,
        Guid automationId,
        int position,
        AutomationAction action,
        string value) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        AutomationId = automationId,
        Position = position,
        Action = action,
        ActionValue = value
    };
}

public enum AutomationRunStatus
{
    Succeeded = 1,
    Skipped = 2,
    Failed = 3,

    /// <summary>A rewrite tried to fire another rule past <see cref="Automation.MaxChainDepth"/>.</summary>
    LoopPrevented = 4
}

/// <summary>One execution attempt, kept so an operator can answer "why did that happen?".</summary>
public sealed class AutomationRun : ITenantOwned
{
    private AutomationRun()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid AutomationId { get; private set; }

    public Guid? ItemId { get; private set; }

    public AutomationRunStatus Status { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static AutomationRun Create(
        Guid tenantId,
        Guid automationId,
        Guid? itemId,
        AutomationRunStatus status,
        string message) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        AutomationId = automationId,
        ItemId = itemId,
        Status = status,
        Message = message.Length > 500 ? message[..500] : message,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
