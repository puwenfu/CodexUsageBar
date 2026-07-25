using Microsoft.Win32;

namespace CodexUsageBar.Windows.Startup;

public sealed class StartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "CodexUsageBar";
    private readonly string _command;
    private readonly IRegistryValueStore _store;

    public StartupRegistration(string executablePath)
        : this(new CurrentUserRegistryValueStore(), executablePath)
    {
    }

    public StartupRegistration(IRegistryValueStore store, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        _store = store;
        _command = $"\"{executablePath}\"";
    }

    public bool IsEnabled =>
        string.Equals(
            _store.GetValue(RunKeyPath, StartupValueName),
            _command,
            StringComparison.Ordinal);

    public void SetEnabled(bool enabled)
    {
        var currentValue = _store.GetValue(RunKeyPath, StartupValueName);
        if (enabled)
        {
            if (!string.Equals(currentValue, _command, StringComparison.Ordinal))
            {
                _store.SetValue(RunKeyPath, StartupValueName, _command);
            }

            return;
        }

        if (currentValue is not null)
        {
            _store.DeleteValue(RunKeyPath, StartupValueName);
        }
    }

    private sealed class CurrentUserRegistryValueStore : IRegistryValueStore
    {
        public string? GetValue(string keyPath, string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            return key?.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        public void SetValue(string keyPath, string valueName, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            key.SetValue(valueName, value, RegistryValueKind.String);
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
