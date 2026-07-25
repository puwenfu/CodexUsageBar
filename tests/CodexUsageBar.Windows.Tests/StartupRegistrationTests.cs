using CodexUsageBar.Windows.Startup;

namespace CodexUsageBar.Windows.Tests;

public sealed class StartupRegistrationTests
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexUsageBar";
    private const string ExecutablePath = @"C:\Path With Spaces\CodexUsageBar.exe";
    private const string QuotedExecutablePath = "\"C:\\Path With Spaces\\CodexUsageBar.exe\"";

    [Fact]
    public void Constructor_DoesNotReadOrWriteRegistry()
    {
        var store = new RecordingRegistryValueStore();

        _ = new StartupRegistration(store, ExecutablePath);

        Assert.Empty(store.Operations);
    }

    [Fact]
    public void SetEnabled_True_WritesQuotedExecutablePathToExactRunValue()
    {
        var store = new RecordingRegistryValueStore();
        var registration = new StartupRegistration(store, ExecutablePath);

        registration.SetEnabled(true);

        Assert.Equal(
            [
                $"get:{RunKeyPath}:{ValueName}",
                $"set:{RunKeyPath}:{ValueName}:{QuotedExecutablePath}",
            ],
            store.Operations);
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void SetEnabled_True_IsIdempotentWhenExactValueAlreadyExists()
    {
        var store = new RecordingRegistryValueStore();
        store.Seed(RunKeyPath, ValueName, QuotedExecutablePath);
        var registration = new StartupRegistration(store, ExecutablePath);

        registration.SetEnabled(true);

        Assert.Equal([$"get:{RunKeyPath}:{ValueName}"], store.Operations);
    }

    [Fact]
    public void SetEnabled_False_DeletesOnlyTheOwnedValueAndIsIdempotent()
    {
        var store = new RecordingRegistryValueStore();
        store.Seed(RunKeyPath, ValueName, QuotedExecutablePath);
        store.Seed(RunKeyPath, "AnotherApp", "another.exe");
        var registration = new StartupRegistration(store, ExecutablePath);

        registration.SetEnabled(false);
        registration.SetEnabled(false);

        Assert.Null(store.Peek(RunKeyPath, ValueName));
        Assert.Equal("another.exe", store.Peek(RunKeyPath, "AnotherApp"));
        Assert.Equal(
            [
                $"get:{RunKeyPath}:{ValueName}",
                $"delete:{RunKeyPath}:{ValueName}",
                $"get:{RunKeyPath}:{ValueName}",
            ],
            store.Operations);
    }

    private sealed class RecordingRegistryValueStore : IRegistryValueStore
    {
        private readonly Dictionary<(string KeyPath, string ValueName), string> _values = [];

        public List<string> Operations { get; } = [];

        public string? GetValue(string keyPath, string valueName)
        {
            Operations.Add($"get:{keyPath}:{valueName}");
            return Peek(keyPath, valueName);
        }

        public void SetValue(string keyPath, string valueName, string value)
        {
            Operations.Add($"set:{keyPath}:{valueName}:{value}");
            _values[(keyPath, valueName)] = value;
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            Operations.Add($"delete:{keyPath}:{valueName}");
            _values.Remove((keyPath, valueName));
        }

        public void Seed(string keyPath, string valueName, string value) =>
            _values[(keyPath, valueName)] = value;

        public string? Peek(string keyPath, string valueName) =>
            _values.GetValueOrDefault((keyPath, valueName));
    }
}
