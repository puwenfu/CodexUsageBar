using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Taskbar;

internal enum TaskbarMessageAction
{
    None,
    PreventActivation,
    RelocateNow,
    RelocateAfterExplorerRestart,
}

internal static class TaskbarWindowPolicy
{
    internal static readonly TimeSpan ExplorerRecoveryInterval = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan ExplorerRecoveryTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan WindowRecoveryInterval = TimeSpan.FromMilliseconds(250);
    internal const uint PositionFlags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
    internal const uint WindowRecoveryPositionFlags =
        NativeMethods.SWP_NOMOVE |
        NativeMethods.SWP_NOSIZE |
        NativeMethods.SWP_NOACTIVATE |
        NativeMethods.SWP_SHOWWINDOW;
    internal const int MouseActivateResult = NativeMethods.MA_NOACTIVATE;

    internal static long ApplyExtendedStyles(long currentStyles) =>
        (currentStyles | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) &
        ~NativeMethods.WS_EX_APPWINDOW;

    internal static long ApplyWindowStyles(long currentStyles) =>
        (currentStyles | NativeMethods.WS_POPUP) & ~NativeMethods.WS_CHILD;

    internal static bool HasRequiredExtendedStyles(long styles) =>
        (styles & NativeMethods.WS_EX_TOOLWINDOW) != 0 &&
        (styles & NativeMethods.WS_EX_NOACTIVATE) != 0 &&
        (styles & NativeMethods.WS_EX_APPWINDOW) == 0;

    internal static bool HasRequiredWindowStyles(long styles) =>
        (styles & NativeMethods.WS_POPUP) != 0 &&
        (styles & NativeMethods.WS_CHILD) == 0;

    internal static bool ShouldRestoreWindowVisibility(
        bool expectedVisible,
        bool visibleAndNotMinimized,
        bool cloaked) =>
        expectedVisible && (!visibleAndNotMinimized || cloaked);

    internal static bool ShouldRestartAfterWindowLoss(
        nint lostTaskbarHandle,
        nint candidateTaskbarHandle,
        int consecutiveObservations) =>
        lostTaskbarHandle != 0 &&
        candidateTaskbarHandle != 0 &&
        candidateTaskbarHandle != lostTaskbarHandle &&
        consecutiveObservations >= 2;

    internal static TaskbarMessageAction GetMessageAction(int message, int taskbarCreatedMessage)
    {
        if (message == NativeMethods.WM_MOUSEACTIVATE)
        {
            return TaskbarMessageAction.PreventActivation;
        }

        if (taskbarCreatedMessage != 0 && message == taskbarCreatedMessage)
        {
            return TaskbarMessageAction.RelocateAfterExplorerRestart;
        }

        return message is NativeMethods.WM_DPICHANGED or NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_SETTINGCHANGE
            ? TaskbarMessageAction.RelocateNow
            : TaskbarMessageAction.None;
    }
}
