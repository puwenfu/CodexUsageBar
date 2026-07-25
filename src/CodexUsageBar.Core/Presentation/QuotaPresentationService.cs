using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.Core.Presentation;

public sealed class QuotaPresentationService(IClock clock, TimeZoneInfo timeZone)
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public WidgetDisplayModel Create(
        QuotaSnapshot? snapshot,
        WidgetStatus status,
        DateTimeOffset? lastSuccessfulAt,
        string? recoveryHint)
    {
        var now = TimeZoneInfo.ConvertTime(clock.Now, timeZone);
        var isStale = snapshot is not null && now - TimeZoneInfo.ConvertTime(snapshot.CapturedAt, timeZone) > StaleAfter;
        var opacity = status == WidgetStatus.Incompatible && isStale
            ? 0.55
            : 1.0;

        var fiveHour = CreateWindow(snapshot?.FiveHour, "5h", now, opacity);
        var weekly = CreateWindow(snapshot?.Weekly, "周", now, opacity);
        var tooltip = CreateTooltip(snapshot, lastSuccessfulAt, recoveryHint);

        return new WidgetDisplayModel(fiveHour, weekly, tooltip, status == WidgetStatus.Refreshing, isStale);
    }

    private QuotaDisplayWindow CreateWindow(
        QuotaWindow? window,
        string label,
        DateTimeOffset now,
        double opacity)
    {
        if (window is null)
        {
            return new QuotaDisplayWindow("--", label, "--", opacity);
        }

        return new QuotaDisplayWindow(
            $"{window.RemainingPercent}%",
            label,
            FormatReset(window.ResetsAt, now),
            opacity);
    }

    private string FormatReset(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null)
        {
            return "--";
        }

        var remaining = reset.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "0m";
        }

        if (remaining.Days > 0)
        {
            return $"{remaining.Days}d {remaining.Hours}h {remaining.Minutes}m";
        }

        if (remaining.Hours > 0)
        {
            return $"{remaining.Hours}h {remaining.Minutes}m";
        }

        return $"{remaining.Minutes}m";
    }

    private string CreateTooltip(QuotaSnapshot? snapshot, DateTimeOffset? lastSuccessfulAt, string? recoveryHint)
    {
        var fiveHourReset = FormatFullTimestamp(snapshot?.FiveHour?.ResetsAt);
        var weeklyReset = FormatFullTimestamp(snapshot?.Weekly?.ResetsAt);
        var lastSuccess = lastSuccessfulAt is null ? "--" : FormatFullTimestamp(lastSuccessfulAt);

        var lines = new List<string>
        {
            $"5小时恢复：{fiveHourReset}",
            $"每周恢复：{weeklyReset}",
            $"最后成功查询：{lastSuccess}",
        };

        if (!string.IsNullOrWhiteSpace(recoveryHint))
        {
            lines.Add(recoveryHint);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatFullTimestamp(DateTimeOffset? value) =>
        value is null ? "--" : TimeZoneInfo.ConvertTime(value.Value, timeZone).ToString("yyyy-MM-dd HH:mm:ss");
}
