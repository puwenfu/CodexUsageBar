namespace CodexUsageBar.App.Services;

internal interface IWidgetPreferences
{
    bool HideFiveHourQuota { get; set; }
}

internal sealed class SessionWidgetPreferences(bool hideFiveHourQuota = false) : IWidgetPreferences
{
    public bool HideFiveHourQuota { get; set; } = hideFiveHourQuota;
}
