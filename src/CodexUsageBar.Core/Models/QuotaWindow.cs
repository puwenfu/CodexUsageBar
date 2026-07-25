namespace CodexUsageBar.Core.Models;

public enum QuotaWindowKind
{
    FiveHour,
    Weekly,
}

public sealed record QuotaMeasurement(
    long? WindowDurationMinutes,
    int UsedPercent,
    long? ResetsAtUnixSeconds);

public sealed record QuotaWindow(
    QuotaWindowKind Kind,
    long WindowDurationMinutes,
    int UsedPercent,
    int RemainingPercent,
    DateTimeOffset? ResetsAt);
