using System.IO;
using System.Text.Json;
using CodexUsageBar.App.Diagnostics;

namespace CodexUsageBar.App.Services;

internal sealed class JsonWidgetPreferences : IWidgetPreferences
{
    private readonly string _settingsPath;
    private readonly IDiagnosticLogger _logger;
    private bool _hideFiveHourQuota;
    private RefreshAnimationStyle _refreshAnimationStyle;

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
        var stored = TryLoad();
        _hideFiveHourQuota = stored.HideFiveHourQuota;
        _refreshAnimationStyle = stored.RefreshAnimationStyle;
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

    public RefreshAnimationStyle RefreshAnimationStyle
    {
        get => _refreshAnimationStyle;
        set
        {
            if (_refreshAnimationStyle == value || !Enum.IsDefined(value))
            {
                return;
            }

            _refreshAnimationStyle = value;
            TrySave();
        }
    }

    private LoadedPreferences TryLoad()
    {
        if (!File.Exists(_settingsPath))
        {
            return LoadedPreferences.Default;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var stored = JsonSerializer.Deserialize<StoredPreferences>(json);
            return stored is null
                ? LoadedPreferences.Default
                : new LoadedPreferences(
                    stored.HideFiveHourQuota,
                    ParseRefreshAnimationStyle(stored.RefreshAnimationStyle));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LogFailure("settings.read_failed", exception);
            return LoadedPreferences.Default;
        }
    }

    private void TrySave()
    {
        var temporaryPath = _settingsPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(
                new StoredPreferences(
                    _hideFiveHourQuota,
                    _refreshAnimationStyle.ToString()));
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

    private static RefreshAnimationStyle ParseRefreshAnimationStyle(string? value) =>
        Enum.TryParse<RefreshAnimationStyle>(value, ignoreCase: false, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : RefreshAnimationStyle.ProgressRing;

    private sealed record StoredPreferences(
        bool HideFiveHourQuota,
        string? RefreshAnimationStyle = null);

    private sealed record LoadedPreferences(
        bool HideFiveHourQuota,
        RefreshAnimationStyle RefreshAnimationStyle)
    {
        public static LoadedPreferences Default { get; } =
            new(false, RefreshAnimationStyle.ProgressRing);
    }
}
