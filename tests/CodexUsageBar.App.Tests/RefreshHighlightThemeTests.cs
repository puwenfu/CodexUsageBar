using System.Windows;
using System.Windows.Media;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class RefreshHighlightThemeTests
{
    [Theory]
    [InlineData("QuotaTheme.xaml")]
    [InlineData("QuotaThemePurple.xaml")]
    [InlineData("QuotaThemeRose.xaml")]
    [InlineData("QuotaThemeMint.xaml")]
    [InlineData("QuotaThemeForest.xaml")]
    public void RefreshHighlight_IsBrighterThanProgressAcrossTheme(string themeName) => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                $"/CodexUsageBar.App;component/Themes/{themeName}",
                UriKind.Relative),
        };

        var progress = Assert.IsType<LinearGradientBrush>(resources["QuotaProgressBrush"]);
        var highlight = Assert.IsType<LinearGradientBrush>(resources["QuotaRefreshHighlightBrush"]);

        Assert.Equal(progress.GradientStops.Count, highlight.GradientStops.Count);
        for (var index = 0; index < progress.GradientStops.Count; index++)
        {
            Assert.True(
                PerceivedBrightness(highlight.GradientStops[index].Color)
                > PerceivedBrightness(progress.GradientStops[index].Color));
        }
    });

    private static double PerceivedBrightness(Color color) =>
        (0.299d * color.R) + (0.587d * color.G) + (0.114d * color.B);
}
