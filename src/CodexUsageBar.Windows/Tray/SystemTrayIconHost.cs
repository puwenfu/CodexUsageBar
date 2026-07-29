using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Tray;

public sealed class SystemTrayIconHost : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = NativeMethods.WM_APP + 24;
    private const string TaskbarCreatedMessageName = "TaskbarCreated";
    private readonly Window _window;
    private readonly Func<SystemTrayMenuAnchor?, bool> _showContextMenu;
    private readonly ISystemTrayNativeApi _nativeApi;
    private HwndSource? _source;
    private nint _windowHandle;
    private int _taskbarCreatedMessage;
    private bool _isVisible;
    private bool _trayMenuSessionActive;
    private bool _isDisposed;

    public SystemTrayIconHost(Window window, Action showContextMenu)
        : this(window, WrapContextMenuAction(showContextMenu))
    {
    }

    public SystemTrayIconHost(Window window, Func<bool> showContextMenu)
        : this(window, WrapContextMenuFunction(showContextMenu))
    {
    }

    public SystemTrayIconHost(
        Window window,
        Func<SystemTrayMenuAnchor?, bool> showContextMenu)
        : this(window, showContextMenu, new SystemTrayNativeApi())
    {
    }

    internal SystemTrayIconHost(
        Window window,
        Func<SystemTrayMenuAnchor?, bool> showContextMenu,
        ISystemTrayNativeApi nativeApi)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _showContextMenu = showContextMenu ??
            throw new ArgumentNullException(nameof(showContextMenu));
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        window.Dispatcher.VerifyAccess();
        window.SourceInitialized += OnSourceInitialized;
        _ = new WindowInteropHelper(window).EnsureHandle();
        InitializeWindowSource();
    }

    private static Func<SystemTrayMenuAnchor?, bool> WrapContextMenuAction(
        Action showContextMenu)
    {
        ArgumentNullException.ThrowIfNull(showContextMenu);
        return _ =>
        {
            showContextMenu();
            return true;
        };
    }

    private static Func<SystemTrayMenuAnchor?, bool> WrapContextMenuFunction(
        Func<bool> showContextMenu)
    {
        ArgumentNullException.ThrowIfNull(showContextMenu);
        return _ => showContextMenu();
    }

    public bool IsVisible => _isVisible;

    public void NotifyContextMenuActivity(bool isOpen)
    {
        if (!isOpen)
        {
            CompleteTrayMenuSession();
        }
    }

    public bool SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _window.Dispatcher.VerifyAccess();
        if (_isVisible == visible)
        {
            return true;
        }

        if (visible)
        {
            var added = AddIcon();
            _isVisible = added;
            return added;
        }

        DeleteIcon();
        _isVisible = false;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_window.Dispatcher.CheckAccess())
        {
            Detach();
        }
        else if (!_window.Dispatcher.HasShutdownStarted)
        {
            _window.Dispatcher.Invoke(Detach);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs) => InitializeWindowSource();

    private void InitializeWindowSource()
    {
        if (_windowHandle != 0)
        {
            return;
        }

        _windowHandle = new WindowInteropHelper(_window).Handle;
        if (_windowHandle == 0)
        {
            return;
        }

        _taskbarCreatedMessage = checked((int)_nativeApi.RegisterWindowMessage(
            TaskbarCreatedMessageName));
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
        if (message == CallbackMessage && TryHandleTrayCallback(wParam, lParam))
        {
            handled = true;
        }
        else if (_isVisible &&
            _taskbarCreatedMessage != 0 &&
            message == _taskbarCreatedMessage)
        {
            _isVisible = AddIcon();
        }

        return 0;
    }

    internal bool TryHandleTrayCallback(nint wParam, nint lParam)
    {
        if (!IsContextMenuNotification(lParam))
        {
            return false;
        }

        BeginTrayMenuSession(GetVersionFourAnchor(wParam, lParam));
        return true;
    }

    internal static bool IsContextMenuNotification(nint lParam)
    {
        var value = unchecked((ulong)lParam.ToInt64());
        var notificationCode = unchecked((int)(value & 0xFFFF));
        var iconId = unchecked((uint)((value >> 16) & 0xFFFF));
        return (iconId == 0 || iconId == IconId) &&
            notificationCode is NativeMethods.WM_RBUTTONUP or NativeMethods.WM_CONTEXTMENU;
    }

    internal static SystemTrayMenuAnchor? GetVersionFourAnchor(
        nint wParam,
        nint lParam)
    {
        var notificationValue = unchecked((ulong)lParam.ToInt64());
        var iconId = unchecked((uint)((notificationValue >> 16) & 0xFFFF));
        if (iconId != IconId)
        {
            return null;
        }

        var anchorValue = unchecked((ulong)wParam.ToInt64());
        return new SystemTrayMenuAnchor(
            unchecked((short)(anchorValue & 0xFFFF)),
            unchecked((short)((anchorValue >> 16) & 0xFFFF)));
    }

    private void BeginTrayMenuSession(SystemTrayMenuAnchor? anchor)
    {
        if (_trayMenuSessionActive)
        {
            return;
        }

        _trayMenuSessionActive = true;
        _ = _nativeApi.SetForegroundWindow(_windowHandle);
        if (!_showContextMenu(anchor))
        {
            CompleteTrayMenuSession();
        }
    }

    private void CompleteTrayMenuSession()
    {
        if (!_trayMenuSessionActive)
        {
            return;
        }

        _trayMenuSessionActive = false;
        if (_windowHandle != 0)
        {
            _ = _nativeApi.PostMessage(_windowHandle, NativeMethods.WM_NULL);
        }
    }

    private bool AddIcon()
    {
        if (_windowHandle == 0)
        {
            return false;
        }

        var data = CreateIconData();
        if (data.hIcon == 0 ||
            !_nativeApi.NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            return false;
        }

        data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        _ = _nativeApi.NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
        return true;
    }

    private void DeleteIcon()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var data = CreateIconData();
        _ = _nativeApi.NotifyIcon(NativeMethods.NIM_DELETE, ref data);
    }

    private NativeMethods.NOTIFYICONDATA CreateIconData() =>
        new()
        {
            cbSize = checked((uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>()),
            hWnd = _windowHandle,
            uID = IconId,
            uFlags = NativeMethods.NIF_MESSAGE |
                NativeMethods.NIF_ICON |
                NativeMethods.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _nativeApi.LoadApplicationIcon(),
            szTip = "Codex Usage Bar",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

    private void Detach()
    {
        CompleteTrayMenuSession();
        if (_isVisible)
        {
            DeleteIcon();
            _isVisible = false;
        }

        _source?.RemoveHook(WindowProcedure);
        _source = null;
        _window.SourceInitialized -= OnSourceInitialized;
        _windowHandle = 0;
    }
}
