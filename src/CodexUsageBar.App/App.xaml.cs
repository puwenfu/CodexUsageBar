using System.Diagnostics;
using System.IO;
using System.Windows;
using CodexUsageBar.App.Diagnostics;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.CodexProtocol.Protocol;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Core.Time;
using CodexUsageBar.Windows;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Startup;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.App;

public partial class App : Application
{
    private const string ApplicationName = "CodexUsageBar";
    private AppRuntime? _runtime;
    private int _exitStarted;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        await StartApplicationAsync();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        var runtime = Interlocked.Exchange(ref _runtime, null);
        if (runtime is not null)
        {
            DispatcherTaskPump.Wait(runtime.DisposeAsync().AsTask(), Dispatcher);
        }

        base.OnExit(eventArgs);
    }

    private async Task StartApplicationAsync()
    {
        await using var startupResources = new StartupResourceScope();
        DiagnosticLogger? logger = null;
        try
        {
            var guard = SingleInstanceGuard.TryAcquire(ApplicationName);
            if (guard is null)
            {
                Shutdown(0);
                return;
            }

            startupResources.Own(guard);
            var clock = new SystemClock();
            var presentation = new QuotaPresentationService(clock, TimeZoneInfo.Local);
            var client = startupResources.Own<ICodexQuotaClient>(new CodexQuotaClient());
            logger = startupResources.Own(new DiagnosticLogger(clock));
            var preferences = new JsonWidgetPreferences(logger);
            var systemThemeWatcher = startupResources.Own<ISystemThemeWatcher>(
                new WindowsSystemThemeWatcher());
            SystemThemeResources.Replace(Resources, systemThemeWatcher.CurrentTheme);
            var debugViewModel = new DebugViewModel();
            var coordinator = startupResources.Own(
                new RefreshCoordinator(client, presentation, clock, logger, debugViewModel));
            startupResources.Forget(client);

            var taskbarLocator = new TaskbarLocator();
            if (!taskbarLocator.TryGetPrimary(out var taskbar))
            {
                throw new InvalidOperationException("The supported primary taskbar is unavailable.");
            }

            var placement = TaskbarPlacementCalculator.Calculate(taskbar.Rectangle, taskbar.Dpi);
            var initialDisplay = presentation.Create(
                snapshot: null,
                WidgetStatus.Ready,
                lastSuccessfulAt: null,
                recoveryHint: null);
            var viewModel = new WidgetViewModel(initialDisplay, placement.RingDiameterDip);
            coordinator.Attach(viewModel);

            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The executable path is unavailable.");
            var startupRegistration = new StartupRegistration(executablePath);
            var window = new WidgetWindow(
                viewModel,
                placement.HeightDip,
                coordinator,
                startupRegistration,
                preferences,
                RequestExit,
                debugViewModel,
                systemThemeWatcher);
            startupResources.Forget(systemThemeWatcher);
            startupResources.Own(new DelegateDisposable(window.Close));

            var host = startupResources.Own(new TaskbarWindowHost());
            host.WindowLost += OnTaskbarWindowLost;
            host.Attach(window);

            var runtime = new AppRuntime(coordinator, host, logger, guard, window.Close);
            startupResources.ReleaseAll();
            _runtime = runtime;
            await coordinator.StartAsync();
        }
        catch (Exception exception)
        {
            logger?.Write(
                new DiagnosticEvent("startup.failed", "startup", 0, string.Empty, null),
                exception);
            await startupResources.DisposeAsync();
            Shutdown(1);
        }
    }

    private async void RequestExit()
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        var runtime = Interlocked.Exchange(ref _runtime, null);
        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }

        Shutdown(0);
    }

    private async void OnTaskbarWindowLost(object? sender, EventArgs eventArgs)
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        var executablePath = Environment.ProcessPath;
        var runtime = Interlocked.Exchange(ref _runtime, null);
        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }

        var restarted = false;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                using var process = Process.Start(
                    new ProcessStartInfo(executablePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(executablePath)
                            ?? Environment.CurrentDirectory,
                    });
                restarted = process is not null;
            }
            catch
            {
                // Explorer recovery falls back to a clean exit if relaunch is unavailable.
            }
        }

        Shutdown(restarted ? 0 : 1);
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
