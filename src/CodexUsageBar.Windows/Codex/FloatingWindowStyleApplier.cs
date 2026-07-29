using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Codex;

internal static class FloatingWindowStyleApplier
{
    internal static bool TryApply(
        IWindowsNativeApi nativeApi,
        nint windowHandle,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!nativeApi.TrySetWindowParent(windowHandle, 0))
        {
            failureReason = "DetachParent";
            return false;
        }

        if (!nativeApi.TryGetWindowExtendedStyle(windowHandle, out var extendedStyles))
        {
            failureReason = "ReadExtendedStyle";
            return false;
        }

        if (!nativeApi.TrySetWindowExtendedStyle(
                windowHandle,
                TaskbarWindowPolicy.ApplyExtendedStyles(extendedStyles)))
        {
            failureReason = "WriteExtendedStyle";
            return false;
        }

        if (!nativeApi.TryGetWindowStyle(windowHandle, out var windowStyles))
        {
            failureReason = "ReadWindowStyle";
            return false;
        }

        var requestedWindowStyles =
            (windowStyles | NativeMethods.WS_POPUP) & ~NativeMethods.WS_CHILD;
        if (!nativeApi.TrySetWindowStyle(windowHandle, requestedWindowStyles))
        {
            failureReason = "WriteWindowStyle";
            return false;
        }

        if (!nativeApi.TryGetWindowStyle(windowHandle, out var appliedWindowStyles))
        {
            failureReason = "ReadBackWindowStyle";
            return false;
        }

        if ((appliedWindowStyles & NativeMethods.WS_POPUP) == 0 ||
            (appliedWindowStyles & NativeMethods.WS_CHILD) != 0)
        {
            failureReason = "VerifyWindowStyle";
            return false;
        }

        return true;
    }
}
