using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.Tests;

public sealed class DebugViewModelTests
{
    private static readonly WidgetDisplayModel Original = new(
        new QuotaDisplayWindow("70%", "5h", "2h", 0.55),
        new QuotaDisplayWindow("40%", "周", "2d", 0.55),
        "真实额度",
        IsRefreshing: true,
        IsStale: true);

    [Fact]
    public void OverrideDisplay_WhenDisabled_PreservesOriginalDisplay()
    {
        var viewModel = new DebugViewModel();

        var result = viewModel.OverrideDisplay(Original);

        Assert.Same(Original, result);
    }

    [Fact]
    public void OverrideDisplay_WhenEnabled_UsesAllConfiguredPreviewValues()
    {
        var viewModel = new DebugViewModel
        {
            FiveHourPercentage = 63,
            FiveHourText = "45m",
            WeeklyPercentage = 27,
            WeeklyText = "3d 4h",
            IsEnabled = true,
        };

        var result = viewModel.OverrideDisplay(Original);

        Assert.Equal("63%", result.FiveHour.PercentageText);
        Assert.Equal("45m", result.FiveHour.ResetText);
        Assert.Equal("27%", result.Weekly.PercentageText);
        Assert.Equal("3d 4h", result.Weekly.ResetText);
        Assert.Equal("调试模式模拟数据", result.Tooltip);
        Assert.False(result.IsRefreshing);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void PreviewValueChanges_RaiseDataChangedOnlyWhileEnabled()
    {
        var viewModel = new DebugViewModel();
        var changes = 0;
        viewModel.DataChanged += (_, _) => changes++;

        viewModel.FiveHourPercentage = 63;
        viewModel.IsEnabled = true;
        viewModel.WeeklyText = "3d 4h";
        viewModel.IsEnabled = false;
        viewModel.FiveHourText = "45m";

        Assert.Equal(3, changes);
    }
}
