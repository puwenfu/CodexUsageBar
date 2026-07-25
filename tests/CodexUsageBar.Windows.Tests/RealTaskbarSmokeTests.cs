using System.Runtime.InteropServices;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class RealTaskbarSmokeTests
{
    [InteractiveTaskbarFact]
    public void ProductionLocator_ReadsCurrentPrimaryBottomTaskbar()
    {
        Assert.True(new TaskbarLocator().TryGetPrimary(out var taskbar));
        Assert.NotEqual(nint.Zero, taskbar.WindowHandle);
        Assert.True(taskbar.Rectangle.Width > 0);
        Assert.True(taskbar.Rectangle.Height > 0);
        Assert.True(taskbar.Dpi > 0);
        Assert.True(taskbar.MonitorBounds.Width > 0);
        Assert.True(taskbar.MonitorBounds.Height > 0);
    }
}

public sealed class InteractiveTaskbarFactAttribute : FactAttribute
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    public InteractiveTaskbarFactAttribute()
    {
        if (FindWindow("Shell_TrayWnd", null) == nint.Zero)
        {
            Skip = "Interactive primary taskbar is unavailable in this Windows session (Shell_TrayWnd was not found).";
        }
    }
}
