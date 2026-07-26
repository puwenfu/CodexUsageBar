namespace CodexUsageBar.App.Services;

internal interface IWidgetPreferences
{
    bool HideFiveHourQuota { get; set; }

    RefreshAnimationStyle RefreshAnimationStyle { get; set; }
}

internal sealed class SessionWidgetPreferences(
    bool hideFiveHourQuota = false,
    RefreshAnimationStyle refreshAnimationStyle = RefreshAnimationStyle.ProgressRing) : IWidgetPreferences
{
    public bool HideFiveHourQuota { get; set; } = hideFiveHourQuota;

    public RefreshAnimationStyle RefreshAnimationStyle { get; set; } = refreshAnimationStyle;
}
