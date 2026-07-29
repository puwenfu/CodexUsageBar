using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Tray;

namespace CodexUsageBar.Windows.Tests;

public sealed class SystemTrayIconHostTests
{
    [Fact]
    public void SetVisible_AddsIconAndNegotiatesVersionFour() => RunSta(() =>
    {
        var native = new RecordingSystemTrayNativeApi();
        var window = new Window();
        using var host = new SystemTrayIconHost(window, _ => true, native);

        Assert.True(host.SetVisible(true));

        Assert.Equal(
            [NativeMethods.NIM_ADD, NativeMethods.NIM_SETVERSION],
            native.NotifyMessages);
        Assert.Equal(
            NativeMethods.NOTIFYICON_VERSION_4,
            native.NotifyVersions[1]);
        window.Close();
    });

    [Fact]
    public void TrayMenuSession_ActivatesBeforeOpeningAndPostsAfterClose() => RunSta(() =>
    {
        var sequence = new List<string>();
        var native = new RecordingSystemTrayNativeApi(sequence);
        var window = new Window();
        using var host = new SystemTrayIconHost(
            window,
            _ =>
            {
                sequence.Add("show");
                return true;
            },
            native);

        Assert.True(host.TryHandleTrayCallback(0, new nint(NativeMethods.WM_RBUTTONUP)));
        Assert.Equal(["foreground", "show"], sequence);

        host.NotifyContextMenuActivity(isOpen: false);

        Assert.Equal(["foreground", "show", "post:0"], sequence);
        window.Close();
    });

    [Theory]
    [InlineData(0x0205)]
    [InlineData(0x0001_007B)]
    public void ContextMenuNotification_AcceptsMouseAndVersionFourKeyboardEvents(
        long notification)
    {
        Assert.True(SystemTrayIconHost.IsContextMenuNotification(new nint(notification)));
    }

    [Fact]
    public void VersionFourCallback_ForwardsSignedScreenAnchor() => RunSta(() =>
    {
        SystemTrayMenuAnchor? receivedAnchor = null;
        var native = new RecordingSystemTrayNativeApi();
        var window = new Window();
        using var host = new SystemTrayIconHost(
            window,
            anchor =>
            {
                receivedAnchor = anchor;
                return true;
            },
            native);
        var wParam = new nint(
            unchecked((ushort)-120) |
            (unchecked((ushort)840) << 16));
        var lParam = new nint(
            NativeMethods.WM_CONTEXTMENU |
            (1 << 16));

        Assert.True(host.TryHandleTrayCallback(wParam, lParam));

        Assert.Equal(new SystemTrayMenuAnchor(-120, 840), receivedAnchor);
        window.Close();
    });

    [Theory]
    [InlineData(0x0201)]
    [InlineData(0x0002_007B)]
    public void ContextMenuNotification_RejectsOtherEventsAndIconIds(long notification)
    {
        Assert.False(SystemTrayIconHost.IsContextMenuNotification(new nint(notification)));
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("STA test did not complete within 10 seconds.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class RecordingSystemTrayNativeApi(
        List<string>? sequence = null) : ISystemTrayNativeApi
    {
        public List<uint> NotifyMessages { get; } = [];

        public List<uint> NotifyVersions { get; } = [];

        public uint RegisterWindowMessage(string messageName) => 0xC123;

        public bool NotifyIcon(
            uint message,
            ref NativeMethods.NOTIFYICONDATA data)
        {
            NotifyMessages.Add(message);
            NotifyVersions.Add(data.uTimeoutOrVersion);
            return true;
        }

        public nint LoadApplicationIcon() => new(1);

        public bool SetForegroundWindow(nint windowHandle)
        {
            sequence?.Add("foreground");
            return true;
        }

        public bool PostMessage(nint windowHandle, int message)
        {
            sequence?.Add($"post:{message}");
            return true;
        }
    }
}
