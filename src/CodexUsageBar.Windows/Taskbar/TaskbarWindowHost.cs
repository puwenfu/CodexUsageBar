using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Taskbar;

public sealed class TaskbarWindowHost : IDisposable
{
    private const string TaskbarCreatedMessageName = "TaskbarCreated";
    private readonly TaskbarLocator _taskbarLocator;
    private readonly TaskbarVisibilityMonitor _visibilityMonitor;
    private readonly IWindowsNativeApi _nativeApi;
    private Window? _window;
    private HwndSource? _source;
    private DispatcherTimer? _explorerRecoveryTimer;
    private DispatcherTimer? _windowRecoveryTimer;
    private DateTime _explorerRecoveryDeadlineUtc;
    private nint _windowHandle;
    private nint _attachedTaskbarHandle;
    private nint _lastAttachedTaskbarHandle;
    private PhysicalRect? _lastTaskbarRectangle;
    private uint _lastTaskbarDpi;
    private TaskbarInfo? _observedTaskbar;
    private TaskbarInfo? _windowLossTaskbarCandidate;
    private int _windowLossTaskbarCandidateObservations;
    private int _taskbarCreatedMessage;
    private bool _desiredVisibility = true;
    private bool? _reportedVisibility;
    private bool _isDisposed;

    public TaskbarWindowHost()
        : this(WindowsNativeApi.Instance)
    {
    }

    private TaskbarWindowHost(IWindowsNativeApi nativeApi)
        : this(new TaskbarLocator(nativeApi), new TaskbarVisibilityMonitor(), nativeApi)
    {
    }

    internal TaskbarWindowHost(
        TaskbarLocator taskbarLocator,
        TaskbarVisibilityMonitor visibilityMonitor,
        IWindowsNativeApi nativeApi)
    {
        _taskbarLocator = taskbarLocator ?? throw new ArgumentNullException(nameof(taskbarLocator));
        _visibilityMonitor = visibilityMonitor ?? throw new ArgumentNullException(nameof(visibilityMonitor));
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        _visibilityMonitor.VisibilityChanged += OnTaskbarVisibilityChanged;
    }

    public event EventHandler<bool>? VisibilityChanged;

    public event EventHandler? WindowLost;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_window is not null)
        {
            throw new InvalidOperationException("A taskbar host can attach only one window.");
        }

        window.Dispatcher.VerifyAccess();
        _window = window;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.SourceInitialized += OnSourceInitialized;

        var helper = new WindowInteropHelper(window);
        _ = helper.EnsureHandle();
        if (_windowHandle == 0)
        {
            InitializeWindowSource();
        }

        _ = Relocate();
    }

    public bool Relocate()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var window = _window;
        if (window is null || _windowHandle == 0)
        {
            return false;
        }

        if (!window.Dispatcher.CheckAccess())
        {
            return window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished
                ? false
                : window.Dispatcher.Invoke(Relocate);
        }

        if (!_taskbarLocator.TryGetPrimary(out var taskbar))
        {
            HandleTaskbarUnavailable();
            return false;
        }

        ObserveTaskbar(taskbar);
        _desiredVisibility = _visibilityMonitor.IsVisible;
        if (!_desiredVisibility)
        {
            SetWindowVisible(false);
            return false;
        }

        TaskbarPlacement placement;
        try
        {
            placement = TaskbarPlacementCalculator.Calculate(taskbar.Rectangle, taskbar.Dpi);
        }
        catch (ArgumentOutOfRangeException)
        {
            SetWindowVisible(false);
            return false;
        }

        window.Width = placement.WidthDip;
        window.Height = placement.HeightDip;

        if (!TaskbarWindowStyleApplier.TryApply(
                _nativeApi,
                _windowHandle,
                taskbar.WindowHandle))
        {
            ResetTaskbarAttachmentState();
            SetWindowVisible(false);
            return false;
        }

        SetWindowVisible(true);

        if (!TaskbarWindowStyleApplier.TryApply(
                _nativeApi,
                _windowHandle,
                taskbar.WindowHandle))
        {
            ResetTaskbarAttachmentState();
            SetWindowVisible(false);
            return false;
        }

        if (!_nativeApi.TryScreenToClient(
                taskbar.WindowHandle,
                placement.LeftPhysicalPixel,
                placement.TopPhysicalPixel,
                out var clientLeft,
                out var clientTop))
        {
            ResetTaskbarAttachmentState();
            SetWindowVisible(false);
            return false;
        }

        var clientBounds = TaskbarWindowPolicy.ToTaskbarClientBounds(
            placement,
            clientLeft,
            clientTop);
        var positioned = _nativeApi.TrySetWindowPosition(
            _windowHandle,
            NativeMethods.HWND_TOP,
            clientBounds,
            TaskbarWindowPolicy.PositionFlags);
        if (!positioned)
        {
            ResetTaskbarAttachmentState();
            SetWindowVisible(false);
            return false;
        }

        _attachedTaskbarHandle = taskbar.WindowHandle;
        _lastAttachedTaskbarHandle = taskbar.WindowHandle;
        _lastTaskbarRectangle = taskbar.Rectangle;
        _lastTaskbarDpi = taskbar.Dpi;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _visibilityMonitor.VisibilityChanged -= OnTaskbarVisibilityChanged;
        _visibilityMonitor.Dispose();

        var window = _window;
        if (window is null)
        {
            return;
        }

        if (window.Dispatcher.CheckAccess())
        {
            DetachWindow();
        }
        else if (!window.Dispatcher.HasShutdownStarted)
        {
            window.Dispatcher.Invoke(DetachWindow);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs) => InitializeWindowSource();

    private void InitializeWindowSource()
    {
        if (_window is null || _windowHandle != 0)
        {
            return;
        }

        _windowHandle = new WindowInteropHelper(_window).Handle;
        if (_windowHandle == 0)
        {
            return;
        }

        _taskbarCreatedMessage = checked((int)NativeMethods.RegisterWindowMessage(TaskbarCreatedMessageName));
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowProcedure);

        _explorerRecoveryTimer = new DispatcherTimer(
            TaskbarWindowPolicy.ExplorerRecoveryInterval,
            DispatcherPriority.Background,
            OnExplorerRecoveryTick,
            _window.Dispatcher);
        _explorerRecoveryTimer.Stop();

        _windowRecoveryTimer = new DispatcherTimer(
            TaskbarWindowPolicy.WindowRecoveryInterval,
            DispatcherPriority.Background,
            OnWindowRecoveryTick,
            _window.Dispatcher);
        _windowRecoveryTimer.Start();
    }

    private void OnWindowRecoveryTick(object? sender, EventArgs eventArgs) => RecoverWindow();

    internal void RecoverWindow()
    {
        var window = _window;
        if (_isDisposed || window is null || _windowHandle == 0)
        {
            return;
        }

        if (!_nativeApi.IsWindow(_windowHandle))
        {
            if (!_taskbarLocator.TryGetPrimary(out var replacementTaskbar))
            {
                ResetWindowLossObservation();
                return;
            }

            if (_windowLossTaskbarCandidate == replacementTaskbar)
            {
                _windowLossTaskbarCandidateObservations++;
            }
            else
            {
                _windowLossTaskbarCandidate = replacementTaskbar;
                _windowLossTaskbarCandidateObservations = 1;
            }

            if (TaskbarWindowPolicy.ShouldRestartAfterWindowLoss(
                    _lastAttachedTaskbarHandle,
                    replacementTaskbar.WindowHandle,
                    _windowLossTaskbarCandidateObservations))
            {
                _windowRecoveryTimer?.Stop();
                WindowLost?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        ResetWindowLossObservation();
        if (!_taskbarLocator.TryGetPrimary(out var taskbar))
        {
            HandleTaskbarUnavailable();
            return;
        }

        ObserveTaskbar(taskbar);
        if (!_desiredVisibility)
        {
            SetWindowVisible(false);
            return;
        }

        if (NeedsRelocation(taskbar) ||
            _reportedVisibility != true ||
            !window.IsVisible)
        {
            _ = Relocate();
            return;
        }

        var visibleAndNotMinimized = _nativeApi.IsWindowVisibleAndNotMinimized(_windowHandle);
        var cloaked = _nativeApi.IsWindowCloaked(_windowHandle);
        if (TaskbarWindowPolicy.ShouldRestoreWindowVisibility(
                _desiredVisibility,
                visibleAndNotMinimized,
                cloaked))
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            _nativeApi.ShowWindowWithoutActivation(_windowHandle);
        }

        _ = _nativeApi.TrySetWindowPosition(
            _windowHandle,
            NativeMethods.HWND_TOP,
            default,
            TaskbarWindowPolicy.WindowRecoveryPositionFlags);
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (_isDisposed)
        {
            return 0;
        }

        var action = TaskbarWindowPolicy.GetMessageAction(message, _taskbarCreatedMessage);
        switch (action)
        {
            case TaskbarMessageAction.PreventActivation:
                handled = true;
                return new nint(TaskbarWindowPolicy.MouseActivateResult);
            case TaskbarMessageAction.RelocateNow:
                _ = Relocate();
                break;
            case TaskbarMessageAction.RelocateAfterExplorerRestart:
                BeginExplorerRecovery();
                break;
        }

        return 0;
    }

    private void BeginExplorerRecovery()
    {
        SetWindowVisible(false);
        if (Relocate())
        {
            return;
        }

        _explorerRecoveryDeadlineUtc = DateTime.UtcNow + TaskbarWindowPolicy.ExplorerRecoveryTimeout;
        _explorerRecoveryTimer?.Start();
    }

    private void OnExplorerRecoveryTick(object? sender, EventArgs eventArgs)
    {
        if (_isDisposed)
        {
            return;
        }

        if (Relocate() || DateTime.UtcNow >= _explorerRecoveryDeadlineUtc)
        {
            _explorerRecoveryTimer?.Stop();
        }
    }

    private void OnTaskbarVisibilityChanged(object? sender, bool isVisible)
    {
        var window = _window;
        if (window is null || _isDisposed)
        {
            return;
        }

        if (window.Dispatcher.CheckAccess())
        {
            ApplyTaskbarVisibility(isVisible);
        }
        else if (!window.Dispatcher.HasShutdownStarted)
        {
            _ = window.Dispatcher.BeginInvoke(() => ApplyTaskbarVisibility(isVisible));
        }
    }

    private void ApplyTaskbarVisibility(bool isVisible)
    {
        if (_isDisposed)
        {
            return;
        }

        _desiredVisibility = isVisible;
        if (isVisible)
        {
            _ = Relocate();
        }
        else
        {
            SetWindowVisible(false);
        }
    }

    private void SetWindowVisible(bool isVisible)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        if (isVisible)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
        }
        else if (window.IsVisible)
        {
            window.Hide();
        }

        if (_reportedVisibility != isVisible)
        {
            _reportedVisibility = isVisible;
            VisibilityChanged?.Invoke(this, isVisible);
        }
    }

    private void DetachWindow()
    {
        _explorerRecoveryTimer?.Stop();
        _explorerRecoveryTimer = null;
        _windowRecoveryTimer?.Stop();
        _windowRecoveryTimer = null;
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        if (_window is not null)
        {
            _window.SourceInitialized -= OnSourceInitialized;
        }

        _window = null;
        _windowHandle = 0;
        _lastAttachedTaskbarHandle = 0;
        _observedTaskbar = null;
        ResetWindowLossObservation();
        ResetTaskbarAttachmentState();
    }

    private bool NeedsRelocation(TaskbarInfo taskbar) =>
        _attachedTaskbarHandle != taskbar.WindowHandle ||
        _lastTaskbarRectangle != taskbar.Rectangle ||
        _lastTaskbarDpi != taskbar.Dpi ||
        _nativeApi.GetWindowParent(_windowHandle) != taskbar.WindowHandle;

    private void ResetTaskbarAttachmentState()
    {
        _attachedTaskbarHandle = 0;
        _lastTaskbarRectangle = null;
        _lastTaskbarDpi = 0;
    }

    private void HandleTaskbarUnavailable()
    {
        _observedTaskbar = null;
        ResetTaskbarAttachmentState();
        _visibilityMonitor.Suspend();
        SetWindowVisible(false);
    }

    private void ObserveTaskbar(TaskbarInfo taskbar)
    {
        if (_observedTaskbar == taskbar)
        {
            return;
        }

        _observedTaskbar = taskbar;
        _visibilityMonitor.Observe(taskbar);
    }

    private void ResetWindowLossObservation()
    {
        _windowLossTaskbarCandidate = null;
        _windowLossTaskbarCandidateObservations = 0;
    }
}
