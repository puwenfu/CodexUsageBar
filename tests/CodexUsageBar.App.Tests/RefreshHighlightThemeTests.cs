using System.Windows;
using System.Windows.Media;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class RefreshHighlightThemeTests
{
    [Theory]
    [InlineData("QuotaTheme.xaml", "QuotaThemeLight.xaml")]
    [InlineData("QuotaThemePurple.xaml", "QuotaThemePurpleLight.xaml")]
    [InlineData("QuotaThemeRose.xaml", "QuotaThemeRoseLight.xaml")]
    [InlineData("QuotaThemeMint.xaml", "QuotaThemeMintLight.xaml")]
    [InlineData("QuotaThemeForest.xaml", "QuotaThemeForestLight.xaml")]
    public void LightProgress_IsBrighterThanDarkProgressAcrossThemes(
        string darkThemeName,
        string lightThemeName) => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var dark = LoadTheme(darkThemeName);
        var light = LoadTheme(lightThemeName);
        var darkProgress = Assert.IsType<LinearGradientBrush>(dark["QuotaProgressBrush"]);
        var lightProgress = Assert.IsType<LinearGradientBrush>(light["QuotaProgressBrush"]);

        Assert.Equal(darkProgress.GradientStops.Count, lightProgress.GradientStops.Count);
        for (var index = 0; index < darkProgress.GradientStops.Count; index++)
        {
            Assert.True(
                PerceivedBrightness(lightProgress.GradientStops[index].Color)
                > PerceivedBrightness(darkProgress.GradientStops[index].Color));
        }
    });

    [Theory]
    [InlineData("QuotaTheme.xaml")]
    [InlineData("QuotaThemePurple.xaml")]
    [InlineData("QuotaThemeRose.xaml")]
    [InlineData("QuotaThemeMint.xaml")]
    [InlineData("QuotaThemeForest.xaml")]
    [InlineData("QuotaThemeLight.xaml")]
    [InlineData("QuotaThemePurpleLight.xaml")]
    [InlineData("QuotaThemeRoseLight.xaml")]
    [InlineData("QuotaThemeMintLight.xaml")]
    [InlineData("QuotaThemeForestLight.xaml")]
    public void RefreshHighlight_IsBrighterThanProgressAcrossTheme(string themeName) => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = LoadTheme(themeName);

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

    private static ResourceDictionary LoadTheme(string themeName) => new()
    {
        Source = new Uri(
            $"/CodexUsageBar.App;component/Themes/{themeName}",
            UriKind.Relative),
    };
}
