using System.Windows;
using System.Windows.Threading;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarWindowHostThreadingTests
{
    [Fact]
    public async Task Relocate_FromBackgroundThread_WithInitialStyleFailure_FailsClosedOnDispatcher()
    {
        using var ui = new WpfDispatcherThread();
        var native = CreateNativeApi();
        native.SetWindowExtendedStyleSucceeds = false;
        var uiThreadId = ui.Invoke(() => Environment.CurrentManagedThreadId);
        var resources = ui.Invoke(() => CreateHostResources(native));
        try
        {
            var relocated = await Task.Run(resources.Host.Relocate);

            Assert.False(relocated);
            Assert.All(native.WindowStyleThreadIds, threadId => Assert.Equal(uiThreadId, threadId));
            Assert.False(ui.Invoke(() => resources.Window.IsVisible));
        }
        finally
        {
            ui.Invoke(() => DisposeHostResources(resources));
        }
    }

    [Fact]
    public async Task Relocate_FromBackgroundThread_WithValidStyles_TouchesNativePlacementOnlyOnDispatcher()
    {
        using var ui = new WpfDispatcherThread();
        var native = CreateNativeApi();
        var uiThreadId = ui.Invoke(() => Environment.CurrentManagedThreadId);
        var resources = ui.Invoke(() => CreateHostResources(native));
        try
        {
            var relocated = await Task.Run(resources.Host.Relocate);

            Assert.True(relocated);
            Assert.All(native.WindowStyleThreadIds, threadId => Assert.Equal(uiThreadId, threadId));
            Assert.All(native.FindWindowThreadIds, threadId => Assert.Equal(uiThreadId, threadId));
            Assert.True(ui.Invoke(() => resources.Window.IsVisible));
        }
        finally
        {
            ui.Invoke(() => DisposeHostResources(resources));
        }
    }

    [Fact]
    public void RecoverWindow_AfterTransientPlacementFailure_RetriesWhileTaskbarShouldBeVisible()
    {
        using var ui = new WpfDispatcherThread();
        var native = CreateNativeApi();
        native.SetWindowPositionSucceeds = false;
        var resources = ui.Invoke(() => CreateHostResources(native));
        try
        {
            Assert.False(ui.Invoke(() => resources.Window.IsVisible));

            native.SetWindowPositionSucceeds = true;
            ui.Invoke(resources.Host.RecoverWindow);

            Assert.True(ui.Invoke(() => resources.Window.IsVisible));
            Assert.True(native.SetWindowPositionCallCount >= 2);
        }
        finally
        {
            ui.Invoke(() => DisposeHostResources(resources));
        }
    }

    [Fact]
    public void Relocate_UsesTaskbarClientCoordinatesReturnedByWindows()
    {
        using var ui = new WpfDispatcherThread();
        var native = CreateNativeApi();
        native.ClientOffsetX = 3;
        native.ClientOffsetY = 3;
        var resources = ui.Invoke(() => CreateHostResources(native));
        try
        {
            var placement = TaskbarPlacementCalculator.Calculate(native.AppBarRectangle, native.Dpi);
            Assert.Equal(
                new PhysicalRect(
                    placement.LeftPhysicalPixel + native.ClientOffsetX,
                    native.ClientOffsetY,
                    placement.RightPhysicalPixel + native.ClientOffsetX,
                    native.ClientOffsetY + placement.BottomPhysicalPixel - placement.TopPhysicalPixel),
                native.LastWindowPosition);
        }
        finally
        {
            ui.Invoke(() => DisposeHostResources(resources));
        }
    }

    [Fact]
    public void RecoverWindow_RaisesWindowLostOnlyAfterReplacementTaskbarIsStable()
    {
        using var ui = new WpfDispatcherThread();
        var native = CreateNativeApi();
        var resources = ui.Invoke(() => CreateHostResources(native));
        var windowLostCount = 0;
        resources.Host.WindowLost += (_, _) => windowLostCount++;
        try
        {
            native.WindowExists = false;
            native.WindowHandle = (nint)84;

            ui.Invoke(resources.Host.RecoverWindow);
            Assert.Equal(0, windowLostCount);

            ui.Invoke(resources.Host.RecoverWindow);
            Assert.Equal(1, windowLostCount);
        }
        finally
        {
            ui.Invoke(() => DisposeHostResources(resources));
        }
    }

    private static FakeWindowsNativeApi CreateNativeApi() =>
        new()
        {
            WindowHandle = (nint)42,
            AppBarRectangle = new PhysicalRect(0, 1032, 1920, 1080),
            AppBarEdge = NativeMethods.ABE_BOTTOM,
            Dpi = 96,
            MonitorRectangle = new PhysicalRect(0, 0, 1920, 1080),
        };

    private static HostResources CreateHostResources(FakeWindowsNativeApi native)
    {
        var window = new Window
        {
            Opacity = 0,
            ShowActivated = false,
        };
        var monitor = new TaskbarVisibilityMonitor(
            _ => native.AppBarRectangle,
            NoOpPeriodicScheduler.Instance);
        var host = new TaskbarWindowHost(new TaskbarLocator(native), monitor, native);
        host.Attach(window);
        return new HostResources(host, window);
    }

    private static void DisposeHostResources(HostResources resources)
    {
        resources.Host.Dispose();
        resources.Window.Close();
    }

    private sealed record HostResources(TaskbarWindowHost Host, Window Window);

    private sealed class NoOpPeriodicScheduler : IPeriodicScheduler
    {
        internal static NoOpPeriodicScheduler Instance { get; } = new();

        public IDisposable Schedule(TimeSpan interval, Action callback) =>
            new CancellationTokenSource();
    }

    private sealed class WpfDispatcherThread : IDisposable
    {
        private readonly Thread _thread;
        private readonly TaskCompletionSource<Dispatcher> _dispatcherSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal WpfDispatcherThread()
        {
            _thread = new Thread(RunDispatcher)
            {
                IsBackground = true,
                Name = "CodexUsageBar.Windows.Tests.WpfDispatcher",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        private Dispatcher Dispatcher => _dispatcherSource.Task.GetAwaiter().GetResult();

        internal T Invoke<T>(Func<T> callback) => Dispatcher.Invoke(callback);

        internal void Invoke(Action callback) => Dispatcher.Invoke(callback);

        public void Dispose()
        {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)), "WPF dispatcher thread did not stop.");
        }

        private void RunDispatcher()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcherSource.SetResult(dispatcher);
            Dispatcher.Run();
        }
    }
}
