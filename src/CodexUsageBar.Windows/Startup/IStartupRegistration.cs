namespace CodexUsageBar.Windows.Startup;

public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
