using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Interop;

internal interface IWindowsNativeApi
{
    nint FindWindow(string className);
    IReadOnlyList<nint> EnumerateTopLevelWindows(uint processId);
    nint GetForegroundWindow();
    bool TryGetWindowRectangle(nint windowHandle, out PhysicalRect rectangle);
    bool TryGetWindowClientRectangle(nint windowHandle, out PhysicalRect rectangle);
    uint GetDpiForWindow(nint windowHandle);
    bool TryGetTaskbarPosition(out uint edge, out PhysicalRect rectangle);
    uint GetTaskbarState();
    bool TryGetMonitorBounds(nint windowHandle, out PhysicalRect rectangle);
    bool TryGetWindowExtendedStyle(nint windowHandle, out long extendedStyle);
    bool TrySetWindowExtendedStyle(nint windowHandle, long extendedStyle);
    bool TryGetWindowStyle(nint windowHandle, out long windowStyle);
    bool TrySetWindowStyle(nint windowHandle, long windowStyle);
    bool TrySetWindowParent(nint windowHandle, nint parentWindowHandle);
    nint GetWindowAbove(nint windowHandle);
    bool TrySetWindowPosition(
        nint windowHandle,
        nint insertAfter,
        PhysicalRect bounds,
        uint flags);
    void ShowWindowWithoutActivation(nint windowHandle);
    bool IsWindow(nint windowHandle);
    bool IsWindowVisibleAndNotMinimized(nint windowHandle);
    bool IsWindowCloaked(nint windowHandle);
}
