using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarLocatorTests
{
    [Fact]
    public void TryGetPrimary_ReturnsBottomTaskbarGeometryDpiMonitorAndAutoHideState()
    {
        var native = new FakeWindowsNativeApi
        {
            WindowHandle = (nint)42,
            AppBarRectangle = new PhysicalRect(0, 1032, 1920, 1080),
            AppBarEdge = NativeMethods.ABE_BOTTOM,
            AppBarState = NativeMethods.ABS_AUTOHIDE,
            Dpi = 144,
            MonitorRectangle = new PhysicalRect(0, 0, 1920, 1080),
        };
        var locator = new TaskbarLocator(native);

        var found = locator.TryGetPrimary(out var info);

        Assert.True(found);
        Assert.Equal((nint)42, info.WindowHandle);
        Assert.Equal(native.AppBarRectangle, info.Rectangle);
        Assert.Equal(144u, info.Dpi);
        Assert.Equal(native.MonitorRectangle, info.MonitorBounds);
        Assert.True(info.IsAutoHide);
        Assert.Equal(["Shell_TrayWnd"], native.FindWindowClassNames);
    }

    [Fact]
    public void TryGetPrimary_RejectsTaskbarOnUnsupportedEdge()
    {
        var native = new FakeWindowsNativeApi
        {
            WindowHandle = (nint)42,
            AppBarRectangle = new PhysicalRect(0, 0, 48, 1080),
            AppBarEdge = 0,
            Dpi = 96,
            MonitorRectangle = new PhysicalRect(0, 0, 1920, 1080),
        };

        var found = new TaskbarLocator(native).TryGetPrimary(out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetPrimary_ReturnsFalseWhenMonitorInformationCannotBeRead()
    {
        var native = new FakeWindowsNativeApi
        {
            WindowHandle = (nint)42,
            AppBarRectangle = new PhysicalRect(0, 1032, 1920, 1080),
            AppBarEdge = NativeMethods.ABE_BOTTOM,
            Dpi = 96,
            MonitorRectangle = null,
        };

        Assert.False(new TaskbarLocator(native).TryGetPrimary(out _));
    }
}
