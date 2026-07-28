namespace CodexUsageBar.App.Services;

internal enum WidgetPlacementPreference
{
    Automatic,
    TaskbarPreferred,
    CodexSidebarPreferred,
    SystemTrayOnly,
}

internal enum QuotaColorTheme
{
    Blue,
    Purple,
    Rose,
    Mint,
    Forest,
}

internal interface IWidgetPreferences
{
    bool HideFiveHourQuota { get; set; }

    QuotaColorTheme ColorTheme { get; set; }

    RefreshAnimationStyle RefreshAnimationStyle { get; set; }

    WidgetPlacementPreference PlacementPreference { get; set; }

    double TaskbarHorizontalOffsetDip { get; set; }

    double CodexSidebarHorizontalOffsetDip { get; set; }
}

internal sealed class SessionWidgetPreferences(
    bool hideFiveHourQuota = false,
    QuotaColorTheme colorTheme = QuotaColorTheme.Blue,
    RefreshAnimationStyle refreshAnimationStyle = RefreshAnimationStyle.ProgressRing,
    WidgetPlacementPreference placementPreference = WidgetPlacementPreference.Automatic,
    double taskbarHorizontalOffsetDip = 0d,
    double codexSidebarHorizontalOffsetDip = 0d) : IWidgetPreferences
{
    private double _taskbarHorizontalOffsetDip =
        WidgetHorizontalOffset.Normalize(taskbarHorizontalOffsetDip);
    private double _codexSidebarHorizontalOffsetDip =
        WidgetHorizontalOffset.Normalize(codexSidebarHorizontalOffsetDip);

    public bool HideFiveHourQuota { get; set; } = hideFiveHourQuota;

    public QuotaColorTheme ColorTheme { get; set; } = colorTheme;

    public RefreshAnimationStyle RefreshAnimationStyle { get; set; } = refreshAnimationStyle;

    public WidgetPlacementPreference PlacementPreference { get; set; } = placementPreference;

    public double TaskbarHorizontalOffsetDip
    {
        get => _taskbarHorizontalOffsetDip;
        set => _taskbarHorizontalOffsetDip = WidgetHorizontalOffset.Normalize(value);
    }

    public double CodexSidebarHorizontalOffsetDip
    {
        get => _codexSidebarHorizontalOffsetDip;
        set => _codexSidebarHorizontalOffsetDip = WidgetHorizontalOffset.Normalize(value);
    }
}
