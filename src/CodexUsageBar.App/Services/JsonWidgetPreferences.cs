using System.IO;
using System.Text.Json;
using CodexUsageBar.App.Diagnostics;

namespace CodexUsageBar.App.Services;

internal sealed class JsonWidgetPreferences : IWidgetPreferences
{
    private readonly string _settingsPath;
    private readonly IDiagnosticLogger _logger;
    private bool _hideFiveHourQuota;

    public JsonWidgetPreferences(IDiagnosticLogger logger)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageBar",
                "settings.json"),
            logger)
    {
    }

    internal JsonWidgetPreferences(string settingsPath, IDiagnosticLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hideFiveHourQuota = TryLoad();
    }

    public bool HideFiveHourQuota
    {
        get => _hideFiveHourQuota;
        set
        {
            if (_hideFiveHourQuota == value)
            {
                return;
            }

            _hideFiveHourQuota = value;
            TrySave();
        }
    }

    private bool TryLoad()
    {
        if (!File.Exists(_settingsPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<StoredPreferences>(json)?.HideFiveHourQuota ?? false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LogFailure("settings.read_failed", exception);
            return false;
        }
    }

    private void TrySave()
    {
        var temporaryPath = _settingsPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(new StoredPreferences(_hideFiveHourQuota));
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LogFailure("settings.write_failed", exception);
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private void LogFailure(string eventCode, Exception exception) =>
        _logger.Write(
            new DiagnosticEvent(eventCode, "settings", 0, string.Empty, null),
            exception);

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record StoredPreferences(bool HideFiveHourQuota);
}
