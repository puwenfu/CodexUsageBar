using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Taskbar;

public sealed class TaskbarLocator
{
    private const string PrimaryTaskbarClassName = "Shell_TrayWnd";
    private readonly IWindowsNativeApi _nativeApi;

    public TaskbarLocator() : this(WindowsNativeApi.Instance)
    {
    }

    internal TaskbarLocator(IWindowsNativeApi nativeApi) => _nativeApi = nativeApi;

    public bool TryGetPrimary(out TaskbarInfo info)
    {
        info = null!;
        var taskbarWindow = _nativeApi.FindWindow(PrimaryTaskbarClassName);
        if (taskbarWindow == 0 ||
            !_nativeApi.TryGetWindowRectangle(taskbarWindow, out var taskbarRectangle) ||
            taskbarRectangle.Width <= 0 ||
            taskbarRectangle.Height <= 0 ||
            taskbarRectangle.Width < taskbarRectangle.Height) // If it's wider than tall, it's bottom/top, assume bottom for now.
        {
            return false;
        }

        var dpi = _nativeApi.GetDpiForWindow(taskbarWindow);
        if (dpi == 0 || !_nativeApi.TryGetMonitorBounds(taskbarWindow, out var monitorBounds))
        {
            return false;
        }

        info = new TaskbarInfo(
            taskbarWindow,
            taskbarRectangle,
            dpi,
            monitorBounds,
            (_nativeApi.GetTaskbarState() & NativeMethods.ABS_AUTOHIDE) != 0);
        return true;
    }
}
