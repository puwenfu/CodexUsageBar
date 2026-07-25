using System.Runtime.InteropServices;
using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Interop;

internal sealed class WindowsNativeApi : IWindowsNativeApi
{
    internal static WindowsNativeApi Instance { get; } = new();

    private WindowsNativeApi()
    {
    }

    public nint FindWindow(string className) => NativeMethods.FindWindow(className, null);

    public bool TryGetWindowRectangle(nint windowHandle, out PhysicalRect rectangle)
    {
        var found = NativeMethods.GetWindowRect(windowHandle, out var nativeRectangle);
        rectangle = ToPhysicalRect(nativeRectangle);
        return found;
    }

    public uint GetDpiForWindow(nint windowHandle) => NativeMethods.GetDpiForWindow(windowHandle);

    public bool TryGetTaskbarPosition(out uint edge, out PhysicalRect rectangle)
    {
        var appBarData = CreateAppBarData();
        var found = NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref appBarData) != 0;
        edge = appBarData.uEdge;
        rectangle = ToPhysicalRect(appBarData.rc);
        return found;
    }

    public uint GetTaskbarState()
    {
        var appBarData = CreateAppBarData();
        return checked((uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref appBarData));
    }

    public bool TryGetMonitorBounds(nint windowHandle, out PhysicalRect rectangle)
    {
        var monitor = NativeMethods.MonitorFromWindow(windowHandle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = checked((uint)Marshal.SizeOf<NativeMethods.MONITORINFO>()),
        };
        var found = monitor != 0 && NativeMethods.GetMonitorInfo(monitor, ref monitorInfo);
        rectangle = ToPhysicalRect(monitorInfo.rcMonitor);
        return found;
    }

    public bool TryGetWindowExtendedStyle(nint windowHandle, out long extendedStyle)
    {
        Marshal.SetLastPInvokeError(0);
        var result = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
        var succeeded = NativeCallResultPolicy.PointerResultSucceeded(result, Marshal.GetLastPInvokeError());
        extendedStyle = result.ToInt64();
        return succeeded;
    }

    public bool TrySetWindowExtendedStyle(nint windowHandle, long extendedStyle)
    {
        Marshal.SetLastPInvokeError(0);
        var result = NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE, new nint(extendedStyle));
        return NativeCallResultPolicy.PointerResultSucceeded(result, Marshal.GetLastPInvokeError());
    }

    public bool TryGetWindowStyle(nint windowHandle, out long windowStyle)
    {
        Marshal.SetLastPInvokeError(0);
        var result = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_STYLE);
        var succeeded = NativeCallResultPolicy.PointerResultSucceeded(result, Marshal.GetLastPInvokeError());
        windowStyle = result.ToInt64();
        return succeeded;
    }

    public bool TrySetWindowStyle(nint windowHandle, long windowStyle)
    {
        Marshal.SetLastPInvokeError(0);
        var result = NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWL_STYLE, new nint(windowStyle));
        return NativeCallResultPolicy.PointerResultSucceeded(result, Marshal.GetLastPInvokeError());
    }

    public bool TrySetWindowParent(nint windowHandle, nint parentWindowHandle)
    {
        Marshal.SetLastPInvokeError(0);
        var result = NativeMethods.SetParent(windowHandle, parentWindowHandle);
        return NativeCallResultPolicy.PointerResultSucceeded(result, Marshal.GetLastPInvokeError());
    }

    public nint GetWindowParent(nint windowHandle) => NativeMethods.GetParent(windowHandle);

    public bool TryScreenToClient(
        nint windowHandle,
        int screenX,
        int screenY,
        out int clientX,
        out int clientY)
    {
        var point = new NativeMethods.POINT
        {
            X = screenX,
            Y = screenY,
        };
        var succeeded = NativeMethods.ScreenToClient(windowHandle, ref point);
        clientX = point.X;
        clientY = point.Y;
        return succeeded;
    }

    public bool TrySetWindowPosition(
        nint windowHandle,
        nint insertAfter,
        PhysicalRect bounds,
        uint flags) =>
        NativeMethods.SetWindowPos(
            windowHandle,
            insertAfter,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            flags);

    public void ShowWindowWithoutActivation(nint windowHandle) =>
        _ = NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_SHOWNOACTIVATE);

    public bool IsWindow(nint windowHandle) => NativeMethods.IsWindow(windowHandle);

    public bool IsWindowVisibleAndNotMinimized(nint windowHandle) =>
        NativeMethods.IsWindowVisible(windowHandle) && !NativeMethods.IsIconic(windowHandle);

    public bool IsWindowCloaked(nint windowHandle) =>
        NativeMethods.DwmGetWindowAttribute(
            windowHandle,
            NativeMethods.DWMWA_CLOAKED,
            out var cloaked,
            sizeof(int)) == 0 &&
        cloaked != 0;

    private static NativeMethods.APPBARDATA CreateAppBarData() =>
        new() { cbSize = checked((uint)Marshal.SizeOf<NativeMethods.APPBARDATA>()) };

    private static PhysicalRect ToPhysicalRect(NativeMethods.RECT rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
