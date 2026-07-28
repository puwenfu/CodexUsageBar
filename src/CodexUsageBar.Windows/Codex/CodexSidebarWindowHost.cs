using System.Windows;
using System.Windows.Interop;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Codex;

public sealed class CodexSidebarWindowHost : IDisposable
{
    private readonly IWindowsNativeApi _nativeApi;
    private Window? _window;
    private HwndSource? _source;
    private nint _windowHandle;
    private bool _isDisposed;

    public string LastActivationFailure { get; private set; } = string.Empty;

    public CodexSidebarWindowHost()
        : this(WindowsNativeApi.Instance)
    {
    }

    internal CodexSidebarWindowHost(IWindowsNativeApi nativeApi) =>
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_window is not null)
        {
            throw new InvalidOperationException("A Codex sidebar host can attach only one window.");
        }

        window.Dispatcher.VerifyAccess();
        _window = window;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.SourceInitialized += OnSourceInitialized;
        _ = new WindowInteropHelper(window).EnsureHandle();
        InitializeWindowSource();
    }

    public bool Activate(CodexSidebarPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var window = _window;
        if (window is null || _windowHandle == 0)
        {
            LastActivationFailure = "WindowUnavailable";
            return false;
        }

        window.Dispatcher.VerifyAccess();
        LastActivationFailure = string.Empty;
        var needsStyleRefresh = !window.IsVisible;
        if (needsStyleRefresh &&
            !FloatingWindowStyleApplier.TryApply(
                _nativeApi,
                _windowHandle,
                out var prepareFailure))
        {
            LastActivationFailure = $"PrepareFloatingStyle.{prepareFailure}";
            return false;
        }

        window.Width = placement.WidthDip;
        window.Height = placement.HeightDip;
        window.MaxHeight = placement.HeightDip;
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (needsStyleRefresh &&
            !FloatingWindowStyleApplier.TryApply(
                _nativeApi,
                _windowHandle,
                out var restoreFailure))
        {
            LastActivationFailure = $"RestoreFloatingStyle.{restoreFailure}";
            window.Hide();
            return false;
        }

        if (needsStyleRefresh &&
            !_nativeApi.TrySetWindowPosition(
                _windowHandle,
                NativeMethods.HWND_NOTOPMOST,
                default,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE))
        {
            LastActivationFailure = "RestoreFloatingZOrder";
            window.Hide();
            return false;
        }

        var positioned = _nativeApi.TrySetWindowPosition(
            _windowHandle,
            GetInsertionAnchor(placement.AnchorWindowHandle),
            placement.Bounds,
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW |
            NativeMethods.SWP_FRAMECHANGED);
        var visible = positioned &&
            _nativeApi.IsWindowVisibleAndNotMinimized(_windowHandle) &&
            !_nativeApi.IsWindowCloaked(_windowHandle);
        if (!visible)
        {
            LastActivationFailure = positioned
                ? "NativeVisibility"
                : "NativePosition";
            window.Hide();
        }
        return visible;
    }

    public void Deactivate()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var window = _window;
        if (window is null)
        {
            return;
        }

        window.Dispatcher.VerifyAccess();
        if (window.IsVisible)
        {
            window.Hide();
        }

        if (_windowHandle != 0)
        {
            _ = _nativeApi.TrySetWindowPosition(
                _windowHandle,
                NativeMethods.HWND_NOTOPMOST,
                default,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE);
        }

    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        var window = _window;
        if (window is not null)
        {
            if (window.Dispatcher.CheckAccess())
            {
                DetachWindow();
            }
            else if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Dispatcher.Invoke(DetachWindow);
            }
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
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowProcedure);
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new nint(TaskbarWindowPolicy.MouseActivateResult);
        }

        return 0;
    }

    private void DetachWindow()
    {
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        if (_window is not null)
        {
            _window.SourceInitialized -= OnSourceInitialized;
        }

        _window = null;
        _windowHandle = 0;
    }

    private nint GetInsertionAnchor(nint anchorWindowHandle)
    {
        var windowAboveAnchor = _nativeApi.GetWindowAbove(anchorWindowHandle);
        if (windowAboveAnchor == _windowHandle)
        {
            windowAboveAnchor = _nativeApi.GetWindowAbove(_windowHandle);
        }

        return windowAboveAnchor == 0
            ? NativeMethods.HWND_TOP
            : windowAboveAnchor;
    }
}
