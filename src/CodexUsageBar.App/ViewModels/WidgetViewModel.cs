using System.ComponentModel;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.ViewModels;

public sealed class WidgetViewModel : INotifyPropertyChanged
{
    private QuotaMeterViewModel fiveHour;
    private QuotaMeterViewModel weekly;
    private string tooltip;
    private bool isRefreshing;
    private bool isStale;

    public WidgetViewModel(WidgetDisplayModel display, double ringDiameterDip)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!double.IsFinite(ringDiameterDip) || ringDiameterDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ringDiameterDip));
        }

        RingDiameterDip = Math.Max(22d, ringDiameterDip);
        fiveHour = new QuotaMeterViewModel(display.FiveHour);
        weekly = new QuotaMeterViewModel(display.Weekly);
        tooltip = display.Tooltip;
        isRefreshing = display.IsRefreshing;
        isStale = display.IsStale;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public QuotaMeterViewModel FiveHour
    {
        get => fiveHour;
    }

    public QuotaMeterViewModel Weekly
    {
        get => weekly;
    }

    public string Tooltip
    {
        get => tooltip;
    }

    public bool IsRefreshing
    {
        get => isRefreshing;
    }

    public bool IsStale
    {
        get => isStale;
    }

    public double RingDiameterDip { get; }

    public void Apply(WidgetDisplayModel display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var nextFiveHour = new QuotaMeterViewModel(display.FiveHour);
        var nextWeekly = new QuotaMeterViewModel(display.Weekly);
        var tooltipChanged = tooltip != display.Tooltip;
        var refreshingChanged = isRefreshing != display.IsRefreshing;
        var staleChanged = isStale != display.IsStale;

        fiveHour = nextFiveHour;
        weekly = nextWeekly;
        tooltip = display.Tooltip;
        isRefreshing = display.IsRefreshing;
        isStale = display.IsStale;

        OnPropertyChanged(nameof(FiveHour));
        OnPropertyChanged(nameof(Weekly));
        if (tooltipChanged)
        {
            OnPropertyChanged(nameof(Tooltip));
        }

        if (refreshingChanged)
        {
            OnPropertyChanged(nameof(IsRefreshing));
        }

        if (staleChanged)
        {
            OnPropertyChanged(nameof(IsStale));
        }
    }

    public void ClearForAccountSwitch()
    {
        Apply(new WidgetDisplayModel(
            new QuotaDisplayWindow("--", "5h", "--", 1),
            new QuotaDisplayWindow("--", "周", "--", 1),
            "正在读取切换后的 Codex 账户额度。",
            IsRefreshing: true,
            IsStale: false));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
