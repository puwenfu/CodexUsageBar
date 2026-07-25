namespace CodexUsageBar.Windows.Startup;

public interface IRegistryValueStore
{
    string? GetValue(string keyPath, string valueName);

    void SetValue(string keyPath, string valueName, string value);

    void DeleteValue(string keyPath, string valueName);
}
