using System.IO;
using System.Text.Json;
using CodexUsageBar.App.Diagnostics;

namespace CodexUsageBar.App.Services;

internal sealed class JsonWidgetPreferences : IWidgetPreferences
{
    private readonly string _settingsPath;
    private readonly IDiagnosticLogger _logger;
    private bool _hideFiveHourQuota;
    private QuotaColorTheme _colorTheme;
    private RefreshAnimationStyle _refreshAnimationStyle;
    private WidgetPlacementPreference _placementPreference;
    private double _taskbarHorizontalOffsetDip;
    private double _codexSidebarHorizontalOffsetDip;

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
        _colorTheme = stored.ColorTheme;
        _refreshAnimationStyle = stored.RefreshAnimationStyle;
        _placementPreference = stored.PlacementPreference;
        _taskbarHorizontalOffsetDip = stored.TaskbarHorizontalOffsetDip;
        _codexSidebarHorizontalOffsetDip = stored.CodexSidebarHorizontalOffsetDip;
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

    public QuotaColorTheme ColorTheme
    {
        get => _colorTheme;
        set
        {
            if (_colorTheme == value || !Enum.IsDefined(value))
            {
                return;
            }

            _colorTheme = value;
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

    public WidgetPlacementPreference PlacementPreference
    {
        get => _placementPreference;
        set
        {
            if (_placementPreference == value || !Enum.IsDefined(value))
            {
                return;
            }

            _placementPreference = value;
            TrySave();
        }
    }

    public double TaskbarHorizontalOffsetDip
    {
        get => _taskbarHorizontalOffsetDip;
        set
        {
            var normalized = WidgetHorizontalOffset.Normalize(value);
            if (_taskbarHorizontalOffsetDip == normalized)
            {
                return;
            }

            _taskbarHorizontalOffsetDip = normalized;
            TrySave();
        }
    }

    public double CodexSidebarHorizontalOffsetDip
    {
        get => _codexSidebarHorizontalOffsetDip;
        set
        {
            var normalized = WidgetHorizontalOffset.Normalize(value);
            if (_codexSidebarHorizontalOffsetDip == normalized)
            {
                return;
            }

            _codexSidebarHorizontalOffsetDip = normalized;
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
                    ParseColorTheme(stored.ColorTheme),
                    ParseRefreshAnimationStyle(stored.RefreshAnimationStyle),
                    ParsePlacementPreference(stored.PlacementPreference),
                    WidgetHorizontalOffset.Normalize(stored.TaskbarHorizontalOffsetDip ?? 0d),
                    WidgetHorizontalOffset.Normalize(stored.CodexSidebarHorizontalOffsetDip ?? 0d));
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
                    _colorTheme.ToString(),
                    _refreshAnimationStyle.ToString(),
                    _placementPreference.ToString(),
                    _taskbarHorizontalOffsetDip,
                    _codexSidebarHorizontalOffsetDip));
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

    private static QuotaColorTheme ParseColorTheme(string? value) =>
        Enum.TryParse<QuotaColorTheme>(value, ignoreCase: false, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : QuotaColorTheme.Blue;

    private static WidgetPlacementPreference ParsePlacementPreference(string? value) =>
        Enum.TryParse<WidgetPlacementPreference>(value, ignoreCase: false, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : WidgetPlacementPreference.Automatic;

    private sealed record StoredPreferences(
        bool HideFiveHourQuota,
        string? ColorTheme = null,
        string? RefreshAnimationStyle = null,
        string? PlacementPreference = null,
        double? TaskbarHorizontalOffsetDip = null,
        double? CodexSidebarHorizontalOffsetDip = null);

    private sealed record LoadedPreferences(
        bool HideFiveHourQuota,
        QuotaColorTheme ColorTheme,
        RefreshAnimationStyle RefreshAnimationStyle,
        WidgetPlacementPreference PlacementPreference,
        double TaskbarHorizontalOffsetDip,
        double CodexSidebarHorizontalOffsetDip)
    {
        public static LoadedPreferences Default { get; } =
            new(
                false,
                QuotaColorTheme.Blue,
                RefreshAnimationStyle.ProgressRing,
                WidgetPlacementPreference.Automatic,
                0d,
                0d);
    }
}
