using CodexUsageBar.Core.Models;

namespace CodexUsageBar.Core.Services;

public static class QuotaSnapshotFactory
{
    private const long FiveHourDurationMinutes = 300;
    private const long WeeklyDurationMinutes = 10_080;

    public static QuotaSnapshot Create(
        IReadOnlyCollection<QuotaMeasurement> measurements,
        DateTimeOffset capturedAt)
    {
        QuotaWindow? fiveHour = null;
        QuotaWindow? weekly = null;

        foreach (var measurement in measurements)
        {
            var window = CreateWindow(measurement);

            switch (window.Kind)
            {
                case QuotaWindowKind.FiveHour when fiveHour is null:
                    fiveHour = window;
                    break;
                case QuotaWindowKind.Weekly when weekly is null:
                    weekly = window;
                    break;
                default:
                    throw new QuotaCompatibilityException($"Duplicate quota window kind: {window.Kind}.");
            }
        }

        return new QuotaSnapshot(fiveHour, weekly, capturedAt);
    }

    private static QuotaWindow CreateWindow(QuotaMeasurement measurement)
    {
        var kind = measurement.WindowDurationMinutes switch
        {
            FiveHourDurationMinutes => QuotaWindowKind.FiveHour,
            WeeklyDurationMinutes => QuotaWindowKind.Weekly,
            _ => throw new QuotaCompatibilityException("Unsupported quota window duration."),
        };

        DateTimeOffset? resetsAt = measurement.ResetsAtUnixSeconds is long resetUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds)
            : null;

        return new QuotaWindow(
            kind,
            measurement.WindowDurationMinutes.Value,
            measurement.UsedPercent,
            (int)Math.Clamp(100L - measurement.UsedPercent, 0L, 100L),
            resetsAt);
    }
}
