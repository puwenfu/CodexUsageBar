using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.Tests;

public sealed class WidgetViewModelTests
{
    [Fact]
    public void Apply_PublishesBothMeterValuesBeforeFirstNotification()
    {
        var viewModel = new WidgetViewModel(CreateDisplay(9, 9), 36);
        (string FiveHour, string Weekly)? stateAtFirstNotification = null;
        viewModel.PropertyChanged += (_, _) =>
            stateAtFirstNotification ??= (viewModel.FiveHour.PercentageText, viewModel.Weekly.PercentageText);

        viewModel.Apply(CreateDisplay(10, 99));

        Assert.Equal(("10%", "99%"), stateAtFirstNotification);
    }

    private static WidgetDisplayModel CreateDisplay(int fiveHour, int weekly) => new(
        new QuotaDisplayWindow($"{fiveHour}%", "5h", "00:35", 1),
        new QuotaDisplayWindow($"{weekly}%", "周", "周五 18:20", 1),
        "quota details",
        IsRefreshing: false,
        IsStale: false);
}
