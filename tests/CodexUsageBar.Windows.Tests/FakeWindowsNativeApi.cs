using System.Collections.Concurrent;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Tests;

internal sealed class FakeWindowsNativeApi : IWindowsNativeApi
{
    public nint WindowHandle { get; set; }

    public PhysicalRect AppBarRectangle { get; init; }

    public uint AppBarEdge { get; init; }

    public uint AppBarState { get; init; }

    public uint Dpi { get; init; }

    public PhysicalRect? MonitorRectangle { get; init; }

    public long WindowExtendedStyle { get; set; } = NativeMethods.WS_EX_APPWINDOW | 0x20L;

    public long WindowStyle { get; set; } = 0x1000L;

    public bool GetWindowExtendedStyleSucceeds { get; set; } = true;

    public bool SetWindowExtendedStyleSucceeds { get; set; } = true;

    public bool IgnoreWindowExtendedStyleWrites { get; set; }

    public bool GetWindowStyleSucceeds { get; set; } = true;

    public bool SetWindowStyleSucceeds { get; set; } = true;

    public bool IgnoreWindowStyleWrites { get; set; }

    public bool SetWindowParentSucceeds { get; set; } = true;

    public nint WindowParent { get; set; }

    public bool WindowExists { get; set; } = true;

    public bool WindowVisibleAndNotMinimized { get; set; } = true;

    public bool WindowCloaked { get; set; }

    public bool SetWindowPositionSucceeds { get; set; } = true;

    public int SetWindowPositionCallCount { get; private set; }

    public PhysicalRect? LastWindowPosition { get; private set; }

    public uint LastWindowPositionFlags { get; private set; }

    public int ShowWindowCallCount { get; private set; }

    public Dictionary<uint, IReadOnlyList<nint>> TopLevelWindowsByProcessId { get; } = [];

    public Dictionary<nint, PhysicalRect> WindowRectangles { get; } = [];

    public Dictionary<nint, nint> WindowAboveByWindow { get; } = [];

    public List<nint> WindowParentTargets { get; } = [];

    public List<string> FindWindowClassNames { get; } = [];

    public ConcurrentQueue<int> FindWindowThreadIds { get; } = new();

    public ConcurrentQueue<int> WindowStyleThreadIds { get; } = new();

    public nint FindWindow(string className)
    {
        FindWindowThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        FindWindowClassNames.Add(className);
        return WindowHandle;
    }

    public IReadOnlyList<nint> EnumerateTopLevelWindows(uint processId) =>
        TopLevelWindowsByProcessId.TryGetValue(processId, out var windows)
            ? windows
            : [];

    public bool TryGetWindowRectangle(nint windowHandle, out PhysicalRect rectangle)
    {
        rectangle = WindowRectangles.GetValueOrDefault(windowHandle, AppBarRectangle);
        return true;
    }

    public uint GetDpiForWindow(nint windowHandle) => Dpi;

    public bool TryGetTaskbarPosition(out uint edge, out PhysicalRect rectangle)
    {
        edge = AppBarEdge;
        rectangle = AppBarRectangle;
        return true;
    }

    public uint GetTaskbarState() => AppBarState;

    public bool TryGetMonitorBounds(nint windowHandle, out PhysicalRect rectangle)
    {
        rectangle = MonitorRectangle.GetValueOrDefault();
        return MonitorRectangle.HasValue;
    }

    public bool TryGetWindowExtendedStyle(nint windowHandle, out long extendedStyle)
    {
        WindowStyleThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        extendedStyle = WindowExtendedStyle;
        return GetWindowExtendedStyleSucceeds;
    }

    public bool TrySetWindowExtendedStyle(nint windowHandle, long extendedStyle)
    {
        WindowStyleThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        if (SetWindowExtendedStyleSucceeds && !IgnoreWindowExtendedStyleWrites)
        {
            WindowExtendedStyle = extendedStyle;
        }

        return SetWindowExtendedStyleSucceeds;
    }

    public bool TryGetWindowStyle(nint windowHandle, out long windowStyle)
    {
        WindowStyleThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        windowStyle = WindowStyle;
        return GetWindowStyleSucceeds;
    }

    public bool TrySetWindowStyle(nint windowHandle, long windowStyle)
    {
        WindowStyleThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        if (SetWindowStyleSucceeds && !IgnoreWindowStyleWrites)
        {
            WindowStyle = windowStyle;
        }

        return SetWindowStyleSucceeds;
    }

    public bool TrySetWindowParent(nint windowHandle, nint parentWindowHandle)
    {
        WindowStyleThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        WindowParentTargets.Add(parentWindowHandle);
        if (SetWindowParentSucceeds)
        {
            WindowParent = parentWindowHandle;
        }

        return SetWindowParentSucceeds;
    }

    public nint GetWindowAbove(nint windowHandle) =>
        WindowAboveByWindow.GetValueOrDefault(windowHandle);

    public bool TrySetWindowPosition(
        nint windowHandle,
        nint insertAfter,
        PhysicalRect bounds,
        uint flags)
    {
        SetWindowPositionCallCount++;
        LastWindowInsertAfter = insertAfter;
        LastWindowPosition = bounds;
        LastWindowPositionFlags = flags;
        return SetWindowPositionSucceeds;
    }

    public nint LastWindowInsertAfter { get; private set; }

    public void ShowWindowWithoutActivation(nint windowHandle)
    {
        ShowWindowCallCount++;
        WindowVisibleAndNotMinimized = true;
    }

    public bool IsWindow(nint windowHandle) => WindowExists;

    public bool IsWindowVisibleAndNotMinimized(nint windowHandle) => WindowVisibleAndNotMinimized;

    public bool IsWindowCloaked(nint windowHandle) => WindowCloaked;
}
