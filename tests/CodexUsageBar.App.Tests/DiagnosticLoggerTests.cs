using System.Text;
using System.IO;
using System.Text.Json;
using CodexUsageBar.App.Diagnostics;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.App.Tests;

public sealed class DiagnosticLoggerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageBar.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_EmitsOnlyWhitelistedFieldsAndNeverExceptionContent()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 23, 1, 2, 3, TimeSpan.Zero));
        using var logger = new DiagnosticLogger(_temporaryDirectory, clock);
        var sensitive = new InvalidOperationException(
            "user@example.com access_token {\"quota\":72,\"token\":\"secret\"}");

        logger.Write(
            new DiagnosticEvent(
                "refresh.failed",
                "offline",
                15,
                "0.142.5",
                new PlacementDiagnostic(6, 1040, 168, 40)),
            sensitive);

        var log = File.ReadAllText(
            Path.Combine(_temporaryDirectory, "2026-07-23.log"),
            Encoding.UTF8);
        using var document = JsonDocument.Parse(log);
        var root = document.RootElement;
        Assert.Equal(
            ["timestamp", "eventCode", "statusCategory", "retrySeconds", "codexVersion", "placement"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(clock.Now, root.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Contains("\"eventCode\":\"refresh.failed\"", log, StringComparison.Ordinal);
        Assert.Contains("\"statusCategory\":\"offline\"", log, StringComparison.Ordinal);
        Assert.Contains("\"retrySeconds\":15", log, StringComparison.Ordinal);
        Assert.Contains("\"codexVersion\":\"0.142.5\"", log, StringComparison.Ordinal);
        Assert.Contains("\"left\":6", log, StringComparison.Ordinal);
        Assert.Contains("\"top\":1040", log, StringComparison.Ordinal);
        Assert.Contains("\"width\":168", log, StringComparison.Ordinal);
        Assert.Contains("\"height\":40", log, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.com", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quota", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_PrunesEightDayOldLogButRetainsSevenDayOldLog()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        var eightDaysOld = Path.Combine(_temporaryDirectory, "eight-days.log");
        var sevenDaysOld = Path.Combine(_temporaryDirectory, "seven-days.log");
        File.WriteAllText(eightDaysOld, "old");
        File.WriteAllText(sevenDaysOld, "keep");
        File.SetLastWriteTimeUtc(eightDaysOld, now.UtcDateTime.AddDays(-8));
        File.SetLastWriteTimeUtc(sevenDaysOld, now.UtcDateTime.AddDays(-7));

        using var logger = new DiagnosticLogger(_temporaryDirectory, new FixedClock(now));

        Assert.False(File.Exists(eightDaysOld));
        Assert.True(File.Exists(sevenDaysOld));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
