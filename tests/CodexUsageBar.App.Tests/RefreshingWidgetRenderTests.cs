using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageBar.App.Controls;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class RefreshingWidgetRenderTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void RefreshingWidget_RendersBoundedStaticHaloAtRequiredDpi(int dpi) => StaTest.Run(() =>
    {
        var window = CreateRefreshingWindow(dpi);
        try
        {
            ShowOffscreen(window);
            var meters = FindVisualChildren<QuotaMeter>(window).ToArray();
            Assert.Equal(2, meters.Length);
            Assert.All(meters, meter =>
            {
                meter.ApplyRefreshVisualState(animationsEnabled: false);
                Assert.Equal(36d, meter.ActualWidth, precision: 3);
                Assert.Equal(36d, meter.ActualHeight, precision: 3);
                Assert.Equal(36d, meter.Halo.ActualWidth, precision: 3);
                Assert.Equal(36d, meter.Halo.ActualHeight, precision: 3);
                Assert.Equal(0.24d, meter.Halo.Opacity);
            });

            window.UpdateLayout();
            SaveRefreshing(Render(window, dpi), dpi);
        }
        finally
        {
            window.Close();
        }
    });

    private static WidgetWindow CreateRefreshingWindow(int dpi)
    {
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow("96%", "5h", "57m", 1),
            new QuotaDisplayWindow("96%", "周", "6d 20h 57m", 1),
            "quota details",
            IsRefreshing: true,
            IsStale: false);
        var physicalHeight = checked((int)Math.Round(48d * dpi / 96d));
        var placement = TaskbarPlacementCalculator.Calculate(
            new PhysicalRect(0, 0, 1920, physicalHeight),
            checked((uint)dpi),
            desiredWidthDip: 168);
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
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SaveRefreshing(BitmapSource bitmap, int dpi)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "artifacts", "visual");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"widget-refreshing-{dpi}dpi.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "CodexUsageBar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate CodexUsageBar.sln.");
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
}
