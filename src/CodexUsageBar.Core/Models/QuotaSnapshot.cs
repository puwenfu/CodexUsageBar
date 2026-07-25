namespace CodexUsageBar.Core.Models;

public sealed record QuotaSnapshot(
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    DateTimeOffset CapturedAt);
