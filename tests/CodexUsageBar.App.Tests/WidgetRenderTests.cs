using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageBar.App.Controls;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class WidgetRenderTests
{
    private const double TaskbarHeightDip = 48;

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void ProductionPlacement_UsesThirtySixDipRingForStandardTaskbar(int dpi)
    {
        var placement = CreateProductionPlacement(dpi, desiredWidthDip: 168);

        Assert.Equal(48, placement.HeightDip, precision: 3);
        Assert.Equal(36, placement.RingDiameterDip, precision: 3);
    }

    [Theory]
    [InlineData(96, 9)]
    [InlineData(96, 10)]
    [InlineData(96, 99)]
    [InlineData(96, 100)]
    [InlineData(144, 9)]
    [InlineData(144, 10)]
    [InlineData(144, 99)]
    [InlineData(144, 100)]
    [InlineData(192, 9)]
    [InlineData(192, 10)]
    [InlineData(192, 99)]
    [InlineData(192, 100)]
    public void Widget_RendersDeterministicallyWithoutClipping(int dpi, int percent) => StaTest.Run(() =>
    {
        var window = CreateWindow(percent, dpi);

        try
        {
            ShowOffscreen(window);
            Assert.InRange(window.ActualWidth, 150, 180);
            Assert.True(window.ActualHeight <= TaskbarHeightDip);
            Assert.All(FindVisualChildren<QuotaMeter>(window), meter => Assert.Equal(36, meter.ActualWidth));

            var textBlocks = FindVisualChildren<TextBlock>(window).ToArray();
            Assert.NotEmpty(textBlocks);
            Assert.All(textBlocks, text => Assert.False(IsTextClipped(text), $"Clipped text: {text.Text}"));

            var percentages = textBlocks.Where(text => Equals(text.Tag, "Percentage")).ToArray();
            Assert.Equal(2, percentages.Length);
            Assert.All(percentages, text => Assert.Equal(TextAlignment.Center, text.TextAlignment));
            Assert.All(percentages, text =>
                Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(text)));

            var bitmap = Render(window, dpi);
            Save(bitmap, dpi, percent);
            Assert.False(ContainsColor(bitmap, Color.FromRgb(0x15, 0x15, 0x1B), tolerance: 0));

            if (dpi == 96 && percent == 9)
            {
                Assert.True(ContainsColor(bitmap, Color.FromRgb(0x34, 0x2B, 0x45), tolerance: 8));
            }

            if (dpi == 96 && percent == 100)
            {
                Assert.True(ContainsColor(bitmap, Color.FromRgb(0x8D, 0x9E, 0xFC), tolerance: 38));
                Assert.True(ContainsColor(bitmap, Color.FromRgb(0x4E, 0x4F, 0xF4), tolerance: 38));
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void HiddenFiveHour_RendersWeeklyOnlyWithoutClipping(int dpi) => StaTest.Run(() =>
    {
        var window = CreateWindow(percent: 41, dpi);
        window.FiveHourMeter.Visibility = Visibility.Collapsed;
        window.FiveHourResetText.Visibility = Visibility.Collapsed;

        try
        {
            ShowOffscreen(window);
            var visibleMeters = FindVisualChildren<QuotaMeter>(window)
                .Where(meter => meter.IsVisible)
                .ToArray();
            var visibleResetTexts = FindVisualChildren<AdaptiveResetText>(window)
                .Where(text => text.IsVisible)
                .ToArray();

            Assert.Single(visibleMeters);
            Assert.Single(visibleResetTexts);
            Assert.Equal(36, visibleMeters[0].ActualWidth, precision: 3);
            Assert.InRange(
                GetBoundsInWindow(visibleMeters[0], window).Left,
                1.99,
                2.01);
            Assert.False(IsTextClipped(
                Assert.Single(
                    FindVisualChildren<TextBlock>(visibleResetTexts[0]),
                    text => Equals(text.Tag, "ResetTime"))));

            var bitmap = Render(window, dpi);
            SaveHidden(bitmap, dpi);
            AssertVisiblePixels(bitmap, GetBoundsInWindow(visibleMeters[0], window));
            AssertVisiblePixels(bitmap, GetBoundsInWindow(visibleResetTexts[0], window));
            AssertRightEdgePixelGap(
                bitmap,
                [GetBoundsInWindow(visibleResetTexts[0], window)]);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(150, 96)]
    [InlineData(150, 144)]
    [InlineData(150, 192)]
    [InlineData(168, 96)]
    [InlineData(168, 144)]
    [InlineData(168, 192)]
    [InlineData(180, 96)]
    [InlineData(180, 144)]
    [InlineData(180, 192)]
    public void ProductionFormats_StayVisibleAndInsideAdaptiveLayout(double widthDip, int dpi) => StaTest.Run(() =>
    {
        var scenarios = new[]
        {
            new RenderScenario("common", "00:35", "周五 18:20"),
            new RenderScenario("tomorrow", "明天 18:20", "周五 18:20"),
            new RenderScenario("absolute", "明天 18:20", "12月31日 18:20"),
        };

        foreach (var scenario in scenarios)
        {
            var window = CreateWindow(
                percent: 100,
                dpi,
                widthDip,
                scenario.FiveHourReset,
                scenario.WeeklyReset);

            try
            {
                ShowOffscreen(window);
                Assert.Equal(widthDip, window.ActualWidth, precision: 3);
                Assert.True(window.ActualHeight <= TaskbarHeightDip);
                Assert.All(FindVisualChildren<QuotaMeter>(window), meter => Assert.Equal(36, meter.ActualWidth));

                var blocks = FindVisualChildren<FrameworkElement>(window)
                    .Where(element => element is QuotaMeter or AdaptiveResetText)
                    .Select(element => new RenderBlock(element, GetBoundsInWindow(element, window)))
                    .OrderBy(block => block.Bounds.Left)
                    .ToArray();

                Assert.Equal(4, blocks.Length);
                Assert.All(blocks, block =>
                {
                    Assert.True(block.Bounds.Width > 0, $"Zero-width block: {block.Element.GetType().Name}");
                    Assert.True(block.Bounds.Height > 0, $"Zero-height block: {block.Element.GetType().Name}");
                    Assert.InRange(block.Bounds.Left, -0.01, window.ActualWidth);
                    Assert.InRange(block.Bounds.Right, 0, window.ActualWidth + 0.01);
                    Assert.InRange(block.Bounds.Top, -0.01, window.ActualHeight);
                    Assert.InRange(block.Bounds.Bottom, 0, window.ActualHeight + 0.01);
                });

                for (var index = 0; index < blocks.Length - 1; index++)
                {
                    Assert.True(
                        blocks[index].Bounds.Right <= blocks[index + 1].Bounds.Left + 0.01,
                        $"Overlapping blocks: {blocks[index].Bounds} and {blocks[index + 1].Bounds}");
                }

                var textBlocks = FindVisualChildren<TextBlock>(window).ToArray();
                Assert.All(
                    textBlocks,
                    text => Assert.False(IsTextClipped(text), $"Clipped text: {text.Text}"));

                var resetTexts = textBlocks.Where(text => Equals(text.Tag, "ResetTime")).ToArray();
                Assert.Equal(2, resetTexts.Length);
                Assert.Equal(scenario.FiveHourReset, NormalizeLineBreak(resetTexts[0].Text));
                Assert.Equal(scenario.WeeklyReset, NormalizeLineBreak(resetTexts[1].Text));
                Assert.All(resetTexts, text =>
                {
                    var renderedBounds = GetBoundsInWindow(text, window);
                    var horizontalScale = renderedBounds.Width / text.ActualWidth;
                    var verticalScale = renderedBounds.Height / text.ActualHeight;
                    var effectiveFontSize = text.FontSize * verticalScale;
                    Assert.True(
                        horizontalScale >= 0.78,
                        $"Reset text ScaleX {horizontalScale:0.00} is below 0.78 " +
                        $"at {widthDip:0} DIP width: {NormalizeLineBreak(text.Text)}");
                    Assert.True(
                        effectiveFontSize >= 7.5,
                        $"Effective reset font {effectiveFontSize:0.00} DIP is below " +
                        $"7.50 DIP at {widthDip:0} DIP width: {text.Text}");
                });

                Assert.Equal(
                    scenario.FiveHourReset.Contains(' '),
                    resetTexts[0].Text.Contains(Environment.NewLine, StringComparison.Ordinal));
                Assert.Equal(
                    scenario.WeeklyReset.Contains(' '),
                    resetTexts[1].Text.Contains(Environment.NewLine, StringComparison.Ordinal));

                var percentageTexts = textBlocks.Where(text => Equals(text.Tag, "Percentage")).ToArray();
                Assert.Equal(2, percentageTexts.Length);
                Assert.All(percentageTexts, text =>
                    Assert.True(text.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight()));

                var bitmap = Render(window, dpi);
                Assert.All(
                    textBlocks,
                    text => Assert.True(
                        GetBoundsInWindow(text, window).Left >= 1d / (dpi / 96d),
                        $"Glyph layout reaches the left window edge: {text.Text}"));
                AssertRightEdgePixelGap(
                    bitmap,
                    blocks.Where(block => block.Element is AdaptiveResetText).Select(block => block.Bounds));
                if (scenario.Name is "common" or "absolute")
                {
                    SaveScenario(bitmap, scenario.Name, widthDip, dpi);
                }

                if (widthDip == 150)
                {
                    Assert.All(blocks, block => AssertVisiblePixels(bitmap, block.Bounds));
                }
            }
            finally
            {
                window.Close();
            }
        }
    });

    private static WidgetWindow CreateWindow(
        int percent,
        int dpi,
        double widthDip = 168,
        string fiveHourReset = "00:35",
        string weeklyReset = "周五 18:20")
    {
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow($"{percent}%", "5h", fiveHourReset, 1),
            new QuotaDisplayWindow($"{percent}%", "周", weeklyReset, 1),
            "quota details",
            IsRefreshing: false,
            IsStale: false);

        var placement = CreateProductionPlacement(dpi, widthDip);
        var window = new WidgetWindow(
            new WidgetViewModel(display, placement.RingDiameterDip),
            placement.HeightDip,
            new DebugViewModel())
        {
            Width = placement.WidthDip,
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/QuotaTheme.xaml",
                UriKind.Relative),
        });
        return window;
    }

    private static TaskbarPlacement CreateProductionPlacement(int dpi, double desiredWidthDip)
    {
        var physicalHeight = checked((int)Math.Round(TaskbarHeightDip * dpi / 96d));
        return TaskbarPlacementCalculator.Calculate(
            new PhysicalRect(0, 0, 1920, physicalHeight),
            checked((uint)dpi),
            desiredWidthDip);
    }

    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30_000;
        window.Top = -30_000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
    }

    private static RenderTargetBitmap Render(Window window, int dpi)
    {
        var scale = dpi / 96d;
        var width = (int)Math.Ceiling(window.ActualWidth * scale);
        var height = (int)Math.Ceiling(window.ActualHeight * scale);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(window);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, int dpi, int percent)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "artifacts", "visual");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"widget-{dpi}dpi-{percent}percent.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void SaveScenario(BitmapSource bitmap, string scenario, double widthDip, int dpi)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "artifacts", "visual");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"widget-{scenario}-{widthDip:0}dip-{dpi}dpi.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void SaveHidden(BitmapSource bitmap, int dpi)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "artifacts", "visual");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"widget-weekly-only-{dpi}dpi.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodexUsageBar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate CodexUsageBar.sln.");
    }

    private static bool IsTextClipped(TextBlock text)
    {
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            text.Foreground,
            VisualTreeHelper.GetDpi(text).PixelsPerDip);

        return formatted.WidthIncludingTrailingWhitespace > text.ActualWidth + 0.75
            || formatted.Height > text.ActualHeight + 0.75;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool ContainsColor(BitmapSource bitmap, Color expected, int tolerance)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (Math.Abs(pixels[index] - expected.B) <= tolerance
                && Math.Abs(pixels[index + 1] - expected.G) <= tolerance
                && Math.Abs(pixels[index + 2] - expected.R) <= tolerance
                && pixels[index + 3] > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Rect GetBoundsInWindow(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));

    private static string NormalizeLineBreak(string value) =>
        value.Replace(Environment.NewLine, " ", StringComparison.Ordinal);

    private static void AssertVisiblePixels(BitmapSource bitmap, Rect bounds)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var scale = bitmap.DpiX / 96d;
        var left = Math.Clamp((int)Math.Floor(bounds.Left * scale), 0, bitmap.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(bounds.Top * scale), 0, bitmap.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right * scale), left + 1, bitmap.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * scale), top + 1, bitmap.PixelHeight);

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = (y * stride) + (x * 4);
                var differsFromBackground = Math.Abs(pixels[index] - 0x1B) > 8
                    || Math.Abs(pixels[index + 1] - 0x15) > 8
                    || Math.Abs(pixels[index + 2] - 0x15) > 8;
                if (pixels[index + 3] > 0 && differsFromBackground)
                {
                    return;
                }
            }
        }

        Assert.Fail($"No visible non-background pixels inside {bounds}.");
    }

    private static void AssertRightEdgePixelGap(BitmapSource bitmap, IEnumerable<Rect> textBounds)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var scale = bitmap.DpiX / 96d;
        var edgeX = bitmap.PixelWidth - 1;

        foreach (var bounds in textBounds)
        {
            var top = Math.Clamp((int)Math.Floor(bounds.Top * scale), 0, bitmap.PixelHeight - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * scale), top + 1, bitmap.PixelHeight);
            for (var y = top; y < bottom; y++)
            {
                var index = (y * stride) + (edgeX * 4);
                var differsFromBackground = Math.Abs(pixels[index] - 0x1B) > 8
                    || Math.Abs(pixels[index + 1] - 0x15) > 8
                    || Math.Abs(pixels[index + 2] - 0x15) > 8;
                Assert.False(
                    pixels[index + 3] > 0 && differsFromBackground,
                    $"Visible reset-time glyph reaches physical right edge at ({edgeX}, {y}).");
            }
        }
    }

    private sealed record RenderBlock(FrameworkElement Element, Rect Bounds);

    private sealed record RenderScenario(string Name, string FiveHourReset, string WeeklyReset);
}
