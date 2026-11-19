namespace Tuesday.Web.Components.Ui;

/// <summary>One transient message.</summary>
public sealed record Toast(Guid Id, AlertKind Kind, string Message);

/// <summary>
/// Transient confirmations for interactive surfaces ("Token created", "Update posted").
/// Scoped, so every interactive island on a page — they share one circuit — reaches the same
/// <see cref="ToastHost"/>. Static SSR pages keep using an inline Alert instead.
/// </summary>
public sealed class ToastService
{
    /// <summary>Older toasts are dropped beyond this many, so the stack never covers the page.</summary>
    public const int MaxVisible = 3;

    private readonly List<Toast> _toasts = [];
    private readonly Lock _gate = new();

    public event Action? Changed;

    public IReadOnlyList<Toast> Current
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToList();
            }
        }
    }

    public void Show(string message, AlertKind kind = AlertKind.Success)
    {
        lock (_gate)
        {
            _toasts.Add(new Toast(Guid.NewGuid(), kind, message));

            while (_toasts.Count > MaxVisible)
            {
                _toasts.RemoveAt(0);
            }
        }

        Changed?.Invoke();
    }

    public void Success(string message) => Show(message, AlertKind.Success);

    public void Error(string message) => Show(message, AlertKind.Error);

    public void Dismiss(Guid id)
    {
        bool removed;

        lock (_gate)
        {
            removed = _toasts.RemoveAll(toast => toast.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }
}
