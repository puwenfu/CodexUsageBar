namespace CodexUsageBar.Windows.Taskbar;

public interface IPeriodicScheduler
{
    IDisposable Schedule(TimeSpan interval, Action callback);
}
