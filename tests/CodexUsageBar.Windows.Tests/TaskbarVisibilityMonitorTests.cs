using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarVisibilityMonitorTests
{
    private static readonly PhysicalRect MonitorBounds = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Observe_PollsEveryTwoHundredFiftyMilliseconds(bool isAutoHide)
    {
        var scheduler = new ManualPeriodicScheduler();
        using var monitor = new TaskbarVisibilityMonitor(
            _ => new PhysicalRect(0, 1032, 1920, 1080),
            scheduler);

        monitor.Observe(CreateInfo(isAutoHide));

        Assert.Single(scheduler.Schedules);
        Assert.Equal(TimeSpan.FromMilliseconds(250), scheduler.Schedules[0].Interval);
    }

    [Fact]
    public void Observe_EvaluatesAutoHiddenTaskbarBeforeFirstTimerTick()
    {
        var scheduler = new ManualPeriodicScheduler();
        using var monitor = new TaskbarVisibilityMonitor(
            _ => new PhysicalRect(0, 1078, 1920, 1080),
            scheduler);

        monitor.Observe(CreateInfo(isAutoHide: true));

        Assert.False(monitor.IsVisible);
    }

    [Fact]
    public void Suspend_StopsTheActivePoller()
    {
        var scheduler = new ManualPeriodicScheduler();
        using var monitor = new TaskbarVisibilityMonitor(
            _ => new PhysicalRect(0, 1032, 1920, 1080),
            scheduler);
        monitor.Observe(CreateInfo(isAutoHide: true));

        monitor.Suspend();

        Assert.True(scheduler.Schedules[0].IsDisposed);
    }

    [Theory]
    [InlineData(1078, 1080)]
    [InlineData(1081, 1129)]
    public void Poll_HidesWhenAtMostTwoPixelsAreVisibleOrRectangleIsOutsideMonitor(int top, int bottom)
    {
        var currentRect = new PhysicalRect(0, top, 1920, bottom);
        var scheduler = new ManualPeriodicScheduler();
        using var monitor = new TaskbarVisibilityMonitor(_ => currentRect, scheduler);
        var states = new List<bool>();
        monitor.VisibilityChanged += (_, isVisible) => states.Add(isVisible);
        monitor.Observe(CreateInfo(isAutoHide: true));

        scheduler.Tick();

        Assert.False(monitor.IsVisible);
        Assert.Equal([false], states);
    }

    [Fact]
    public void Poll_RestoresAfterTaskbarBecomesVisible()
    {
        var currentRect = new PhysicalRect(0, 1079, 1920, 1127);
        var scheduler = new ManualPeriodicScheduler();
        using var monitor = new TaskbarVisibilityMonitor(_ => currentRect, scheduler);
        var states = new List<bool>();
        monitor.VisibilityChanged += (_, isVisible) => states.Add(isVisible);
        monitor.Observe(CreateInfo(isAutoHide: true));
        scheduler.Tick();

        currentRect = new PhysicalRect(0, 1032, 1920, 1080);
        scheduler.Tick();

        Assert.True(monitor.IsVisible);
        Assert.Equal([false, true], states);
    }

    private static TaskbarInfo CreateInfo(bool isAutoHide) =>
        new((nint)42, new PhysicalRect(0, 1032, 1920, 1080), 96, MonitorBounds, isAutoHide);

    private sealed class ManualPeriodicScheduler : IPeriodicScheduler
    {
        public List<ScheduledCallback> Schedules { get; } = [];

        public IDisposable Schedule(TimeSpan interval, Action callback)
        {
            var scheduled = new ScheduledCallback(interval, callback);
            Schedules.Add(scheduled);
            return scheduled;
        }

        public void Tick()
        {
            foreach (var schedule in Schedules.Where(item => !item.IsDisposed).ToArray())
            {
                schedule.Callback();
            }
        }
    }

    private sealed class ScheduledCallback(TimeSpan interval, Action callback) : IDisposable
    {
        public TimeSpan Interval { get; } = interval;

        public Action Callback { get; } = callback;

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
