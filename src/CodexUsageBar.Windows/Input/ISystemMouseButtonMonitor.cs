namespace CodexUsageBar.Windows.Input;

public interface ISystemMouseButtonMonitor : IDisposable
{
    event EventHandler<SystemMouseButtonDownEventArgs>? ButtonDown;

    bool Start();

    void Stop();
}

public sealed class SystemMouseButtonDownEventArgs(
    int screenX,
    int screenY) : EventArgs
{
    public int ScreenX { get; } = screenX;

    public int ScreenY { get; } = screenY;
}
