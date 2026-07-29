using System.Reflection;
using System.Runtime.InteropServices;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarWindowHostContractTests
{
    [Fact]
    public void Host_ExposesWindowAttachRelocateVisibilityAndDisposalContract()
    {
        var type = typeof(TaskbarWindowHost);
        var attach = Assert.Single(type.GetMethods(), method => method.Name == "Attach");
        var parameter = Assert.Single(attach.GetParameters());

        Assert.Equal("System.Windows.Window", parameter.ParameterType.FullName);
        Assert.NotNull(type.GetMethod("Relocate", Type.EmptyTypes));
        Assert.NotNull(type.GetEvent("VisibilityChanged"));
        Assert.NotNull(type.GetEvent("WindowLost"));
        Assert.Contains(typeof(IDisposable), type.GetInterfaces());
    }

    [Fact]
    public void ExplorerRecoveryPolicy_RetriesWithinFiveSecondLimit()
    {
        Assert.True(TaskbarWindowPolicy.ExplorerRecoveryInterval > TimeSpan.Zero);
        Assert.True(TaskbarWindowPolicy.ExplorerRecoveryTimeout <= TimeSpan.FromSeconds(5));
        Assert.True(TaskbarWindowPolicy.ExplorerRecoveryInterval < TaskbarWindowPolicy.ExplorerRecoveryTimeout);
    }

    [Fact]
    public void NativeMethods_ContainsOnlyApprovedPInvokeSurface()
    {
        var importedMethods = typeof(NativeMethods)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<DllImportAttribute>() is not null)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CallNextHookEx",
                "DwmGetWindowAttribute",
                "EnumWindows",
                "FindWindow",
                "GetDpiForWindow",
                "GetMonitorInfo",
                "GetModuleHandle",
                "GetWindow",
                "GetWindowThreadProcessId",
                "GetWindowLongPtr",
                "GetWindowRect",
                "IsIconic",
                "IsWindow",
                "IsWindowVisible",
                "LoadIcon",
                "MonitorFromWindow",
                "RegisterWindowMessage",
                "PostMessage",
                "SetForegroundWindow",
                "SetWindowsHookEx",
                "SetWindowLongPtr",
                "SetParent",
                "SetWindowPos",
                "SHAppBarMessage",
                "Shell_NotifyIcon",
                "ShowWindow",
                "UnhookWindowsHookEx",
            }.OrderBy(name => name),
            importedMethods);
    }
}
