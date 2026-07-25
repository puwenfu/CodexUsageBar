using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.Core.Tests;

public sealed class RefreshingPresentationTests
{
    [Fact]
    public void Create_RefreshingKeepsBothMetersAtFullOpacity()
    {
        var now = DateTimeOffset.Parse("2026-07-25T10:00:00+08:00");
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 300, 2, 98, now.AddHours(1)),
            new QuotaWindow(QuotaWindowKind.Weekly, 10_080, 2, 98, now.AddDays(6)),
            now);
        var service = new QuotaPresentationService(
            new FixedClock(now),
            TimeZoneInfo.CreateCustomTimeZone(
                "Refresh-China",
                TimeSpan.FromHours(8),
                "Refresh-China",
                "Refresh-China"));

        var display = service.Create(snapshot, WidgetStatus.Refreshing, now, null);

        Assert.True(display.IsRefreshing);
        Assert.Equal(1d, display.FiveHour.Opacity);
        Assert.Equal(1d, display.Weekly.Opacity);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
