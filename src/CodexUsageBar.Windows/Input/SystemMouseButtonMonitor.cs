using System.Runtime.InteropServices;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Input;

public sealed class SystemMouseButtonMonitor : ISystemMouseButtonMonitor
{
    private readonly NativeMethods.LowLevelMouseProcedure _callback;
    private nint _hookHandle;
    private bool _isDisposed;

    public SystemMouseButtonMonitor()
    {
        _callback = OnMouseMessage;
    }

    public event EventHandler<SystemMouseButtonDownEventArgs>? ButtonDown;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_hookHandle != 0)
        {
            return true;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            threadId: 0);
        return _hookHandle != 0;
    }

    public void Stop()
    {
        if (_hookHandle == 0)
        {
            return;
        }

        _ = NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
    }

    internal static bool IsButtonDownMessage(int message) =>
        message is NativeMethods.WM_LBUTTONDOWN or
            NativeMethods.WM_RBUTTONDOWN or
            NativeMethods.WM_MBUTTONDOWN or
            NativeMethods.WM_XBUTTONDOWN;

    private nint OnMouseMessage(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && IsButtonDownMessage(unchecked((int)wParam.ToInt64())))
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                ButtonDown?.Invoke(
                    this,
                    new SystemMouseButtonDownEventArgs(data.Point.X, data.Point.Y));
            }
            catch
            {
                // A dismissal listener must never interrupt the system input chain.
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }
}
