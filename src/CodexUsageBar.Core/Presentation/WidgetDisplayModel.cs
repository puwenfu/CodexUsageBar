namespace CodexUsageBar.Core.Presentation;

public enum WidgetStatus
{
    Ready,
    Refreshing,
    MissingCodex,
    SignedOut,
    Offline,
    Incompatible,
}

public sealed record QuotaDisplayWindow(
    string PercentageText,
    string Label,
    string ResetText,
    double Opacity);

public sealed record WidgetDisplayModel(
    QuotaDisplayWindow FiveHour,
    QuotaDisplayWindow Weekly,
    string Tooltip,
    bool IsRefreshing,
    bool IsStale);
