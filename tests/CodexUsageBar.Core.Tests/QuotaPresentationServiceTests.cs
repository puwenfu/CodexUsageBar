using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.Core.Tests;

public sealed class QuotaPresentationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-22T10:00:00+08:00");
    private static readonly TimeZoneInfo China = TimeZoneInfo.CreateCustomTimeZone(
        "Plan-China", TimeSpan.FromHours(8), "Plan-China", "Plan-China");

    private static QuotaPresentationService CreateService() =>
        new(new FakeClock(Now), China);

    private static QuotaWindow Window(QuotaWindowKind kind, DateTimeOffset? reset) =>
        new(kind, kind == QuotaWindowKind.FiveHour ? 300 : 10_080, 28, 72, reset);

    [Theory]
    [InlineData("2026-07-22T10:35:00+08:00", "35m")]
    [InlineData("2026-07-22T18:20:00+08:00", "8h 20m")]
    [InlineData("2026-07-23T00:35:00+08:00", "14h 35m")]
    [InlineData("2026-07-24T18:20:00+08:00", "2d 8h 20m")]
    public void Create_FormatsFiveHourReset(string reset, string expected)
    {
        var snapshot = new QuotaSnapshot(
            Window(QuotaWindowKind.FiveHour, DateTimeOffset.Parse(reset)), null, Now);

        var model = CreateService().Create(snapshot, WidgetStatus.Ready, Now, null);

        Assert.Equal(expected, model.FiveHour.ResetText);
    }

    [Theory]
    [InlineData("2026-07-24T18:20:00+08:00", "2d 8h 20m")]
    [InlineData("2026-08-01T18:20:00+08:00", "10d 8h 20m")]
    public void Create_FormatsWeeklyReset(string reset, string expected)
    {
        var snapshot = new QuotaSnapshot(
            null, Window(QuotaWindowKind.Weekly, DateTimeOffset.Parse(reset)), Now);

        var model = CreateService().Create(snapshot, WidgetStatus.Ready, Now, null);

        Assert.Equal(expected, model.Weekly.ResetText);
    }

    [Fact]
    public void Create_ShowsMissingWindowAsDashes()
    {
        var snapshot = new QuotaSnapshot(null, Window(QuotaWindowKind.Weekly, Now.AddDays(2)), Now);

        var model = CreateService().Create(snapshot, WidgetStatus.Ready, Now, null);

        Assert.Equal("--", model.FiveHour.PercentageText);
        Assert.Equal("--", model.FiveHour.ResetText);
    }

    [Fact]
    public void Create_UsesTabularPercentageTextWithoutDecimal()
    {
        var snapshot = new QuotaSnapshot(Window(QuotaWindowKind.FiveHour, Now.AddHours(1)), null, Now);

        var model = CreateService().Create(snapshot, WidgetStatus.Ready, Now, null);

        Assert.Equal("72%", model.FiveHour.PercentageText);
    }

    [Fact]
    public void Create_DimsSnapshotOlderThanFifteenMinutesWhenIncompatible()
    {
        var snapshot = new QuotaSnapshot(Window(QuotaWindowKind.FiveHour, Now.AddHours(1)), null, Now.AddMinutes(-16));

        var model = CreateService().Create(
            snapshot,
            WidgetStatus.Incompatible,
            Now.AddMinutes(-16),
            "额度接口暂不兼容，请更新本工具。");

        Assert.True(model.IsStale);
        Assert.Equal(0.55, model.FiveHour.Opacity);
    }

    [Fact]
    public void Create_TooltipContainsFullResetTimesLastSuccessAndRecoveryHint()
    {
        var snapshot = new QuotaSnapshot(
            Window(QuotaWindowKind.FiveHour, Now.AddHours(1)),
            Window(QuotaWindowKind.Weekly, Now.AddDays(2)),
            Now);

        var model = CreateService().Create(snapshot, WidgetStatus.Offline, Now, "额度读取超时，正在重试。");

        Assert.Contains("2026-07-22 11:00:00", model.Tooltip);
        Assert.Contains("2026-07-24 10:00:00", model.Tooltip);
        Assert.Contains("最后成功查询：2026-07-22 10:00:00", model.Tooltip);
        Assert.Contains("额度读取超时，正在重试。", model.Tooltip);
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
