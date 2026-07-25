using System.ComponentModel;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.ViewModels;

public sealed class DebugViewModel : INotifyPropertyChanged
{
    private bool isEnabled;
    private int fiveHourPercentage = 72;
    private string fiveHourText = "1h 20m";
    private int weeklyPercentage = 41;
    private string weeklyText = "4d 12h 56m";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DataChanged;

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled != value)
            {
                isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int FiveHourPercentage
    {
        get => fiveHourPercentage;
        set
        {
            if (fiveHourPercentage != value)
            {
                fiveHourPercentage = value;
                OnPropertyChanged(nameof(FiveHourPercentage));
                if (IsEnabled) DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string FiveHourText
    {
        get => fiveHourText;
        set
        {
            if (fiveHourText != value)
            {
                fiveHourText = value;
                OnPropertyChanged(nameof(FiveHourText));
                if (IsEnabled) DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int WeeklyPercentage
    {
        get => weeklyPercentage;
        set
        {
            if (weeklyPercentage != value)
            {
                weeklyPercentage = value;
                OnPropertyChanged(nameof(WeeklyPercentage));
                if (IsEnabled) DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string WeeklyText
    {
        get => weeklyText;
        set
        {
            if (weeklyText != value)
            {
                weeklyText = value;
                OnPropertyChanged(nameof(WeeklyText));
                if (IsEnabled) DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public WidgetDisplayModel OverrideDisplay(WidgetDisplayModel original)
    {
        if (!IsEnabled) return original;

        return new WidgetDisplayModel(
            new QuotaDisplayWindow($"{FiveHourPercentage}%", "5h", FiveHourText, 1),
            new QuotaDisplayWindow($"{WeeklyPercentage}%", "周", WeeklyText, 1),
            "调试模式模拟数据",
            false,
            false);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
