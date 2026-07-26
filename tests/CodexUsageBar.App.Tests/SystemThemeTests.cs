using System.Windows;
using System.Windows.Media;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class SystemThemeTests
{
    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 0)]
    [InlineData(null, 1, 1)]
    [InlineData(null, 0, 0)]
    [InlineData(null, null, 0)]
    public void ResolveTheme_PrefersWindowsModeAndFallsBackSafely(
        int? systemUsesLightTheme,
        int? appsUseLightTheme,
        int expected)
    {
        var actual = WindowsSystemThemeWatcher.ResolveTheme(
            systemUsesLightTheme,
            appsUseLightTheme);

        Assert.Equal((SystemTheme)expected, actual);
    }

    [Fact]
    public void LightAndDarkPalettes_ExposeTheSameSemanticKeysWithReadableText() =>
        StaTest.Run(() =>
        {
            var dark = LoadPalette(SystemTheme.Dark);
            var light = LoadPalette(SystemTheme.Light);
            var darkKeys = dark.Keys.Cast<object>().Select(key => key.ToString()).Order().ToArray();
            var lightKeys = light.Keys.Cast<object>().Select(key => key.ToString()).Order().ToArray();

            Assert.Equal(darkKeys, lightKeys);
            AssertPaletteContrast(dark);
            AssertPaletteContrast(light);
            Assert.Equal(
                Color.FromRgb(0x20, 0x21, 0x24),
                AssertBrush(light, "QuotaPrimaryTextBrush").Color);
            Assert.Equal(
                Color.FromRgb(0xB6, 0xB6, 0xB6),
                AssertBrush(light, "QuotaTrackBrush").Color);
            Assert.Equal(
                Color.FromRgb(0x2F, 0x2F, 0x2F),
                AssertBrush(dark, "QuotaTrackBrush").Color);
        });

    [Fact]
    public void ThemeChange_UpdatesLiveWidgetAndUsesLightAccentResource() => StaTest.Run(() =>
    {
        var watcher = new SessionSystemThemeWatcher(SystemTheme.Dark);
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow("72%", "5h", "00:35", 1),
            new QuotaDisplayWindow("41%", "周", "周五 18:20", 1),
            "quota details",
            IsRefreshing: false,
            IsStale: false);
        var window = new WidgetWindow(
            new WidgetViewModel(display, 36),
            48,
            NullRefreshRequester.Instance,
            NullStartupRegistration.Instance,
            new SessionWidgetPreferences(),
            () => { },
            new DebugViewModel(),
            watcher);
        try
        {
            watcher.SetTheme(SystemTheme.Light);

            Assert.EndsWith(
                "/Themes/SystemThemeLight.xaml",
                window.Resources.MergedDictionaries[0].Source?.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(
                Color.FromRgb(0x20, 0x21, 0x24),
                AssertBrush(window.Resources, "QuotaPrimaryTextBrush").Color);

            var applicationResources = new ResourceDictionary();
            WidgetWindow.ReplaceTheme(
                applicationResources,
                WidgetWindow.ResolveThemeResourceName(
                    "QuotaThemePurple.xaml",
                    SystemTheme.Light));
            Assert.EndsWith(
                "/Themes/QuotaThemePurpleLight.xaml",
                Assert.Single(applicationResources.MergedDictionaries).Source?.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData("QuotaTheme.xaml", "QuotaThemeLight.xaml")]
    [InlineData("QuotaThemePurple.xaml", "QuotaThemePurpleLight.xaml")]
    [InlineData("QuotaThemeRose.xaml", "QuotaThemeRoseLight.xaml")]
    [InlineData("QuotaThemeMint.xaml", "QuotaThemeMintLight.xaml")]
    [InlineData("QuotaThemeForest.xaml", "QuotaThemeForestLight.xaml")]
    public void LightTheme_MapsEveryAccentToItsBrighterVariant(
        string darkResourceName,
        string lightResourceName)
    {
        Assert.Equal(
            lightResourceName,
            WidgetWindow.ResolveThemeResourceName(darkResourceName, SystemTheme.Light));
        Assert.Equal(
            darkResourceName,
            WidgetWindow.ResolveThemeResourceName(darkResourceName, SystemTheme.Dark));
    }

    [Fact]
    public void ReplaceSystemTheme_PreservesUnrelatedAndAccentDictionaries() => StaTest.Run(() =>
    {
        var resources = new ResourceDictionary();
        var unrelated = new ResourceDictionary();
        var accent = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/QuotaThemeRose.xaml",
                UriKind.Relative),
        };
        resources.MergedDictionaries.Add(unrelated);
        resources.MergedDictionaries.Add(LoadPalette(SystemTheme.Dark));
        resources.MergedDictionaries.Add(accent);

        SystemThemeResources.Replace(resources, SystemTheme.Light);

        Assert.Equal(3, resources.MergedDictionaries.Count);
        Assert.Same(unrelated, resources.MergedDictionaries[0]);
        Assert.EndsWith(
            "/Themes/SystemThemeLight.xaml",
            resources.MergedDictionaries[1].Source?.ToString(),
            StringComparison.Ordinal);
        Assert.Same(accent, resources.MergedDictionaries[2]);
    });

    private static ResourceDictionary LoadPalette(SystemTheme theme) => new()
    {
        Source = new Uri(
            $"/CodexUsageBar.App;component/Themes/SystemTheme{theme}.xaml",
            UriKind.Relative),
    };

    private static void AssertPaletteContrast(ResourceDictionary resources)
    {
        var surface = Assert.IsType<LinearGradientBrush>(
            resources["ContextMenuSurfaceBrush"]).GradientStops[0].Color;
        Assert.True(
            ContrastRatio(AssertBrush(resources, "ContextMenuPrimaryTextBrush").Color, surface)
            >= 4.5d);
        Assert.True(
            ContrastRatio(AssertBrush(resources, "ContextMenuSecondaryTextBrush").Color, surface)
            >= 4.5d);
    }

    private static SolidColorBrush AssertBrush(ResourceDictionary resources, string key) =>
        Assert.IsType<SolidColorBrush>(resources[key]);

    private static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126d * Linearize(color.R))
        + (0.7152d * Linearize(color.G))
        + (0.0722d * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045d
            ? value / 12.92d
            : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }

    private sealed class NullRefreshRequester : IRefreshRequester
    {
        public static NullRefreshRequester Instance { get; } = new();

        public Task RequestRefresh(RefreshReason reason) => Task.CompletedTask;
    }

    private sealed class NullStartupRegistration : CodexUsageBar.Windows.Startup.IStartupRegistration
    {
        public static NullStartupRegistration Instance { get; } = new();

        public bool IsEnabled => false;

        public void SetEnabled(bool enabled)
        {
        }
    }
}
