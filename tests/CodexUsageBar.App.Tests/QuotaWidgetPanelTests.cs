using System.Windows;
using System.Windows.Controls;
using CodexUsageBar.App.Controls;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class QuotaWidgetPanelTests
{
    [Fact]
    public void CollapsedFiveHourGroup_WeeklyGroupStartsAtLeftAndUsesRemainingWidth() =>
        StaTest.Run(() =>
        {
            var (panel, fiveMeter, fiveReset, weeklyMeter, weeklyReset) = CreatePanel();
            fiveMeter.Visibility = Visibility.Collapsed;
            fiveReset.Visibility = Visibility.Collapsed;

            panel.Measure(new Size(168, 36));
            panel.Arrange(new Rect(0, 0, 168, 36));

            Assert.Equal(0, weeklyMeter.TranslatePoint(new Point(), panel).X, precision: 3);
            Assert.Equal(38, weeklyReset.TranslatePoint(new Point(), panel).X, precision: 3);
            Assert.Equal(129, weeklyReset.ActualWidth, precision: 3);
        });

    [Fact]
    public void RestoredFiveHourGroup_ReturnsToTwoGroupLayout() => StaTest.Run(() =>
    {
        var (panel, fiveMeter, fiveReset, weeklyMeter, _) = CreatePanel();
        fiveMeter.Visibility = Visibility.Collapsed;
        fiveReset.Visibility = Visibility.Collapsed;
        panel.Measure(new Size(168, 36));
        panel.Arrange(new Rect(0, 0, 168, 36));

        fiveMeter.Visibility = Visibility.Visible;
        fiveReset.Visibility = Visibility.Visible;
        panel.Measure(new Size(168, 36));
        panel.Arrange(new Rect(0, 0, 168, 36));

        Assert.Equal(0, fiveMeter.TranslatePoint(new Point(), panel).X, precision: 3);
        Assert.True(weeklyMeter.TranslatePoint(new Point(), panel).X > 38);
        Assert.True(fiveReset.ActualWidth > 0);
    });

    private static (
        QuotaWidgetPanel Panel,
        Border FiveMeter,
        Border FiveReset,
        Border WeeklyMeter,
        Border WeeklyReset) CreatePanel()
    {
        var panel = new QuotaWidgetPanel();
        var fiveMeter = new Border { Width = 36, Height = 36 };
        var fiveReset = new Border { Height = 20 };
        var weeklyMeter = new Border { Width = 36, Height = 36 };
        var weeklyReset = new Border { Height = 20 };
        panel.Children.Add(fiveMeter);
        panel.Children.Add(fiveReset);
        panel.Children.Add(weeklyMeter);
        panel.Children.Add(weeklyReset);
        return (panel, fiveMeter, fiveReset, weeklyMeter, weeklyReset);
    }
}
