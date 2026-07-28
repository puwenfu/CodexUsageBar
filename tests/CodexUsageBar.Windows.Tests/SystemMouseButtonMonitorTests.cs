using CodexUsageBar.Windows.Input;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Tests;

public sealed class SystemMouseButtonMonitorTests
{
    [Theory]
    [InlineData(NativeMethods.WM_LBUTTONDOWN)]
    [InlineData(NativeMethods.WM_RBUTTONDOWN)]
    [InlineData(NativeMethods.WM_MBUTTONDOWN)]
    [InlineData(NativeMethods.WM_XBUTTONDOWN)]
    public void ButtonDownMessages_AreRecognized(int message)
    {
        Assert.True(SystemMouseButtonMonitor.IsButtonDownMessage(message));
    }

    [Theory]
    [InlineData(NativeMethods.WM_RBUTTONUP)]
    [InlineData(NativeMethods.WM_CONTEXTMENU)]
    [InlineData(NativeMethods.WM_NULL)]
    public void NonButtonDownMessages_AreIgnored(int message)
    {
        Assert.False(SystemMouseButtonMonitor.IsButtonDownMessage(message));
    }
}
