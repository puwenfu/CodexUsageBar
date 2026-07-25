using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Taskbar;

public sealed class TaskbarVisibilityMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private readonly Func<nint, PhysicalRect?> _windowRectangleReader;
    private readonly IPeriodicScheduler _scheduler;
    private readonly object _sync = new();
    private IDisposable? _polling;
    private TaskbarInfo? _taskbar;
    private bool _isVisible = true;
    private bool _isDisposed;

    public TaskbarVisibilityMonitor()
        : this(
            ReadWindowRectangle,
            SystemPeriodicScheduler.Instance)
    {
    }

    public TaskbarVisibilityMonitor(
        Func<nint, PhysicalRect?> windowRectangleReader,
        IPeriodicScheduler scheduler)
    {
        _windowRectangleReader = windowRectangleReader ?? throw new ArgumentNullException(nameof(windowRectangleReader));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public event EventHandler<bool>? VisibilityChanged;

    public bool IsVisible
    {
        get
        {
            lock (_sync)
            {
                return _isVisible;
            }
        }
    }

    public void Observe(TaskbarInfo taskbar)
    {
        ArgumentNullException.ThrowIfNull(taskbar);
        IDisposable? oldPolling;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            oldPolling = _polling;
            _taskbar = taskbar;
            _polling = _scheduler.Schedule(PollInterval, Poll);
        }

        oldPolling?.Dispose();
        Poll();
    }

    public void Suspend()
    {
        IDisposable? polling;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            polling = _polling;
            _polling = null;
            _taskbar = null;
        }

        polling?.Dispose();
    }

    public void Dispose()
    {
        IDisposable? polling;
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            polling = _polling;
            _polling = null;
            _taskbar = null;
        }

        polling?.Dispose();
    }

    private void Poll()
    {
        TaskbarInfo? taskbar;
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            taskbar = _taskbar;
        }

        if (taskbar is null)
        {
            return;
        }

        var rectangle = _windowRectangleReader(taskbar.WindowHandle);
        var isTaskbarVisible = taskbar.IsAutoHide
            ? rectangle is { } current && IsSufficientlyVisible(current, taskbar.MonitorBounds)
            : rectangle is not null;
        SetVisibility(isTaskbarVisible);
    }

    private void SetVisibility(bool isVisible)
    {
        lock (_sync)
        {
            if (_isDisposed || _isVisible == isVisible)
            {
                return;
            }

            _isVisible = isVisible;
        }

        VisibilityChanged?.Invoke(this, isVisible);
    }

    private static bool IsSufficientlyVisible(PhysicalRect taskbar, PhysicalRect monitor)
    {
        var visibleWidth = Math.Min(taskbar.Right, monitor.Right) - Math.Max(taskbar.Left, monitor.Left);
        var visibleHeight = Math.Min(taskbar.Bottom, monitor.Bottom) - Math.Max(taskbar.Top, monitor.Top);
        return visibleWidth > 0 && visibleHeight > 2;
    }

    private static PhysicalRect? ReadWindowRectangle(nint windowHandle) =>
        WindowsNativeApi.Instance.TryGetWindowRectangle(windowHandle, out var rectangle) ? rectangle : null;

    private sealed class SystemPeriodicScheduler : IPeriodicScheduler
    {
        internal static SystemPeriodicScheduler Instance { get; } = new();

        public IDisposable Schedule(TimeSpan interval, Action callback) =>
            new Timer(_ => callback(), null, interval, interval);
    }
}
