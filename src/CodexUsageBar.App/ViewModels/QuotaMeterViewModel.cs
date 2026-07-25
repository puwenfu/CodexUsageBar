using System.Globalization;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.ViewModels;

public sealed class QuotaMeterViewModel
{
    public QuotaMeterViewModel(QuotaDisplayWindow display)
    {
        ArgumentNullException.ThrowIfNull(display);
        PercentageText = display.PercentageText;
        Label = display.Label;
        ResetText = display.ResetText;
        ArcOpacity = Math.Clamp(display.Opacity, 0d, 1d);
        Progress = ParseProgress(display.PercentageText);
    }

    public string PercentageText { get; }

    public string Label { get; }

    public string ResetText { get; }

    public double ArcOpacity { get; }

    public double Progress { get; }

    private static double ParseProgress(string text)
    {
        var numeric = text.TrimEnd('%');
        return double.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0d, 100d)
            : 0d;
    }
}
