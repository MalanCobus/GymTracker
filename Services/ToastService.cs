using Microsoft.Extensions.Logging;

namespace GymTracker.Services;

public enum ToastKind
{
    Success,
    Error,
    Info
}

public sealed record ToastItem(Guid Id, ToastKind Kind, string Message);

public interface IToastService
{
    event Action? OnChange;
    IReadOnlyList<ToastItem> Items { get; }

    void ShowSuccess(string message, int durationMs = 2200);
    void ShowError(string message, int durationMs = 3500);
    void ShowInfo(string message, int durationMs = 2500);

    void Dismiss(Guid id);
    void ClearAll();
}

public sealed class ToastService : IToastService, IDisposable
{
    private readonly ILogger<ToastService> _logger;
    private readonly object _gate = new();
    private readonly List<ToastItem> _items = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _timers = new();

    public ToastService(ILogger<ToastService> logger) => _logger = logger;

    public event Action? OnChange;

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_gate) return _items.ToList();
        }
    }

    public void ShowSuccess(string message, int durationMs = 2200)
        => Show(ToastKind.Success, message, durationMs);

    public void ShowError(string message, int durationMs = 3500)
        => Show(ToastKind.Error, message, durationMs);

    public void ShowInfo(string message, int durationMs = 2500)
        => Show(ToastKind.Info, message, durationMs);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(id, out var cts))
            {
                _timers.Remove(id);
                cts.Cancel();
                cts.Dispose();
            }

            _items.RemoveAll(x => x.Id == id);
        }

        NotifyChanged();
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            foreach (var cts in _timers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _timers.Clear();
            _items.Clear();
        }

        NotifyChanged();
    }

    public void Dispose() => ClearAll();

    private void Show(ToastKind kind, string message, int durationMs)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Toast ignored: empty message. Kind={Kind}", kind);
            return;
        }

        var toast = new ToastItem(Guid.NewGuid(), kind, message.Trim());

        CancellationTokenSource cts;
        lock (_gate)
        {
            _items.Add(toast);
            cts = new CancellationTokenSource();
            _timers[toast.Id] = cts;
        }

        _logger.LogInformation("Toast shown. Id={ToastId} Kind={Kind} DurationMs={DurationMs} Message={Message}",
            toast.Id, kind, durationMs, toast.Message);

        NotifyChanged();

        _ = AutoDismissAsync(toast.Id, durationMs, cts.Token);
    }

    private async Task AutoDismissAsync(Guid id, int durationMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Math.Max(250, durationMs), ct);
            Dismiss(id);
        }
        catch (TaskCanceledException tex)
        {
            _logger.LogError(tex, "Toast auto-dismiss canceled. Id={ToastId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toast auto-dismiss failed. Id={ToastId}", id);
        }
    }

    private void NotifyChanged()
    {
        try
        {
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toast change notification failed.");
        }
    }
}
