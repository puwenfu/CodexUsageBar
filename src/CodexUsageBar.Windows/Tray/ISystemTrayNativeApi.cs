using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Tray;

internal interface ISystemTrayNativeApi
{
    uint RegisterWindowMessage(string messageName);

    bool NotifyIcon(uint message, ref NativeMethods.NOTIFYICONDATA data);

    nint LoadApplicationIcon();

    nint CreateProgressIcon(SystemTrayIconState state);

    bool DestroyIcon(nint iconHandle);

    bool SetForegroundWindow(nint windowHandle);

    bool PostMessage(nint windowHandle, int message);
}

internal sealed class SystemTrayNativeApi : ISystemTrayNativeApi
{
    public uint RegisterWindowMessage(string messageName) =>
        NativeMethods.RegisterWindowMessage(messageName);

    public bool NotifyIcon(uint message, ref NativeMethods.NOTIFYICONDATA data) =>
        NativeMethods.Shell_NotifyIcon(message, ref data);

    public nint LoadApplicationIcon()
    {
        var module = NativeMethods.GetModuleHandle(null);
        var icon = module == 0
            ? 0
            : NativeMethods.LoadIcon(module, NativeMethods.IDI_APPLICATION);
        return icon != 0
            ? icon
            : NativeMethods.LoadIcon(0, NativeMethods.IDI_APPLICATION);
    }

    public nint CreateProgressIcon(SystemTrayIconState state) =>
        SystemTrayProgressIconRenderer.CreateIcon(state);

    public bool DestroyIcon(nint iconHandle) =>
        NativeMethods.DestroyIcon(iconHandle);

    public bool SetForegroundWindow(nint windowHandle) =>
        NativeMethods.SetForegroundWindow(windowHandle);

    public bool PostMessage(nint windowHandle, int message) =>
        NativeMethods.PostMessage(windowHandle, message, 0, 0);
}
