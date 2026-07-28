using System.IO;
using CodexUsageBar.App.Diagnostics;
using CodexUsageBar.App.Services;

namespace CodexUsageBar.App.Tests;

public sealed class JsonWidgetPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageBar.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingFile_DefaultsToShowingFiveHourQuota()
    {
        var preferences = Create("settings.json");

        Assert.False(preferences.HideFiveHourQuota);
        Assert.Equal(QuotaColorTheme.Blue, preferences.ColorTheme);
        Assert.Equal(RefreshAnimationStyle.ProgressRing, preferences.RefreshAnimationStyle);
        Assert.Equal(
            WidgetPlacementPreference.Automatic,
            preferences.PlacementPreference);
        Assert.Equal(0d, preferences.TaskbarHorizontalOffsetDip);
        Assert.Equal(0d, preferences.CodexSidebarHorizontalOffsetDip);
    }

    [Fact]
    public void SetColorTheme_PersistsAcrossInstances()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new JsonWidgetPreferences(path, new RecordingLogger());

        first.ColorTheme = QuotaColorTheme.Mint;
        var second = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(QuotaColorTheme.Mint, second.ColorTheme);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SetRefreshAnimationStyle_PersistsAcrossInstances()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new JsonWidgetPreferences(path, new RecordingLogger());

        first.RefreshAnimationStyle = RefreshAnimationStyle.DotOrbit;
        var second = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(RefreshAnimationStyle.DotOrbit, second.RefreshAnimationStyle);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void LegacyFile_DefaultsToProgressRing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"HideFiveHourQuota":true}""");

        var preferences = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.True(preferences.HideFiveHourQuota);
        Assert.Equal(QuotaColorTheme.Blue, preferences.ColorTheme);
        Assert.Equal(RefreshAnimationStyle.ProgressRing, preferences.RefreshAnimationStyle);
        Assert.Equal(
            WidgetPlacementPreference.Automatic,
            preferences.PlacementPreference);
    }

    [Fact]
    public void RemovedColorTheme_FallsBackToBlue()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """{"HideFiveHourQuota":false,"ColorTheme":"Amber"}""");

        var preferences = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(QuotaColorTheme.Blue, preferences.ColorTheme);
    }

    [Fact]
    public void RemovedRefreshStyle_FallsBackToProgressRing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """{"HideFiveHourQuota":false,"RefreshAnimationStyle":"BreathingHalo"}""");

        var preferences = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(RefreshAnimationStyle.ProgressRing, preferences.RefreshAnimationStyle);
    }

    [Fact]
    public void SetHideFiveHourQuota_PersistsAcrossInstances()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new JsonWidgetPreferences(path, new RecordingLogger());

        first.HideFiveHourQuota = true;
        var second = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.True(second.HideFiveHourQuota);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SetPlacementPreference_PersistsAcrossInstances()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new JsonWidgetPreferences(path, new RecordingLogger());

        first.PlacementPreference = WidgetPlacementPreference.CodexSidebarPreferred;
        var second = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(
            WidgetPlacementPreference.CodexSidebarPreferred,
            second.PlacementPreference);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void HorizontalOffsets_PersistIndependentlyAcrossInstances()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new JsonWidgetPreferences(path, new RecordingLogger())
        {
            TaskbarHorizontalOffsetDip = 48d,
            CodexSidebarHorizontalOffsetDip = -24d,
        };

        var second = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(48d, second.TaskbarHorizontalOffsetDip);
        Assert.Equal(-24d, second.CodexSidebarHorizontalOffsetDip);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void RemovedPlacementPreference_FallsBackToAutomatic()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """{"HideFiveHourQuota":false,"PlacementPreference":"DesktopOnly"}""");

        var preferences = new JsonWidgetPreferences(path, new RecordingLogger());

        Assert.Equal(
            WidgetPlacementPreference.Automatic,
            preferences.PlacementPreference);
    }

    [Fact]
    public void CorruptFile_DefaultsToShowingAndLogsReadFailure()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{not-json");
        var logger = new RecordingLogger();

        var preferences = new JsonWidgetPreferences(path, logger);

        Assert.False(preferences.HideFiveHourQuota);
        Assert.Equal(QuotaColorTheme.Blue, preferences.ColorTheme);
        Assert.Equal(RefreshAnimationStyle.ProgressRing, preferences.RefreshAnimationStyle);
        Assert.Equal(
            WidgetPlacementPreference.Automatic,
            preferences.PlacementPreference);
        Assert.Equal(["settings.read_failed"], logger.EventCodes);
    }

    [Fact]
    public void WriteFailure_KeepsSessionValueAndLogsFailure()
    {
        Directory.CreateDirectory(_directory);
        var blockingFile = Path.Combine(_directory, "blocked");
        File.WriteAllText(blockingFile, "file");
        var logger = new RecordingLogger();
        var preferences = new JsonWidgetPreferences(
            Path.Combine(blockingFile, "settings.json"),
            logger);

        preferences.HideFiveHourQuota = true;

        Assert.True(preferences.HideFiveHourQuota);
        Assert.Equal(["settings.write_failed"], logger.EventCodes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonWidgetPreferences Create(string name) =>
        new(Path.Combine(_directory, name), new RecordingLogger());

    private sealed class RecordingLogger : IDiagnosticLogger
    {
        public List<string> EventCodes { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent, Exception? exception = null) =>
            EventCodes.Add(diagnosticEvent.EventCode);

        public void Dispose()
        {
        }
    }
}
