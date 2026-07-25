using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Taskbar;

internal static class TaskbarWindowStyleApplier
{
    internal static bool TryApply(
        IWindowsNativeApi nativeApi,
        nint windowHandle,
        nint taskbarWindowHandle)
    {
        if (!nativeApi.TryGetWindowExtendedStyle(windowHandle, out var currentStyles))
        {
            return false;
        }

        var requestedStyles = TaskbarWindowPolicy.ApplyExtendedStyles(currentStyles);
        if (!nativeApi.TrySetWindowExtendedStyle(windowHandle, requestedStyles) ||
            !nativeApi.TryGetWindowExtendedStyle(windowHandle, out var appliedStyles) ||
            !TaskbarWindowPolicy.HasRequiredExtendedStyles(appliedStyles) ||
            !nativeApi.TryGetWindowStyle(windowHandle, out var currentWindowStyles))
        {
            return false;
        }

        var requestedWindowStyles = TaskbarWindowPolicy.ApplyWindowStyles(currentWindowStyles);
        if (!nativeApi.TrySetWindowStyle(windowHandle, requestedWindowStyles) ||
            !nativeApi.TryGetWindowStyle(windowHandle, out var appliedWindowStyles) ||
            !TaskbarWindowPolicy.HasRequiredWindowStyles(appliedWindowStyles))
        {
            return false;
        }

        return (nativeApi.GetWindowParent(windowHandle) == taskbarWindowHandle ||
                nativeApi.TrySetWindowParent(windowHandle, taskbarWindowHandle)) &&
            nativeApi.GetWindowParent(windowHandle) == taskbarWindowHandle;
    }
}
