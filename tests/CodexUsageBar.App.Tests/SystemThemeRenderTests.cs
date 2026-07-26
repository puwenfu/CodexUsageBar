using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.App.Views;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class SystemThemeRenderTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void LightTheme_RendersWidgetMenuAndDebugPanelAtTargetDpi(int dpi) => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow("72%", "5h", "00:35", 1),
            new QuotaDisplayWindow("41%", "周", "周五 18:20", 1),
            "5小时恢复：00:35\n每周恢复：周五 18:20",
            IsRefreshing: false,
            IsStale: false);
        var window = new WidgetWindow(
            new WidgetViewModel(display, 36),
            48,
            new DebugViewModel());
        var debugWindow = new DebugWindow(new DebugViewModel(), SystemTheme.Light);
        try
        {
            SystemThemeResources.Replace(window.Resources, SystemTheme.Light);
            WidgetWindow.ReplaceTheme(window.Resources, "QuotaThemeLight.xaml");
            ShowOffscreen(window);
            ShowOffscreen(debugWindow);

            var primaryText = Assert.IsType<SolidColorBrush>(
                window.FindResource("QuotaPrimaryTextBrush"));
            Assert.Equal(Color.FromRgb(0x20, 0x21, 0x24), primaryText.Color);
            Assert.Equal(
                Color.FromRgb(0x20, 0x21, 0x24),
                Assert.IsType<SolidColorBrush>(
                    debugWindow.FindResource("ContextMenuPrimaryTextBrush")).Color);

            var widget = CompositeOnLightTaskbar(Render(window, dpi));
            var debug = Render(debugWindow, dpi);
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.ApplyTemplate();
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                item.ApplyTemplate();
            }

            menu.Measure(new Size(240, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            var menuBitmap = Render(menu, dpi);

            Assert.True(widget.PixelWidth > 0);
            Assert.True(debug.PixelWidth > 0);
            Assert.True(menuBitmap.PixelWidth > 0);
            Save(widget, $"system-light-widget-{dpi}dpi.png");
            Save(menuBitmap, $"system-light-menu-{dpi}dpi.png");
            Save(debug, $"system-light-debug-{dpi}dpi.png");
        }
        finally
        {
            debugWindow.Close();
            window.Close();
        }
    });

    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30_000;
        window.Top = -30_000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
    }

    private static RenderTargetBitmap Render(FrameworkElement element, int dpi)
    {
        var scale = dpi / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap CompositeOnLightTaskbar(BitmapSource source)
    {
        var composite = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var size = new Rect(
                0,
                0,
                source.PixelWidth * 96d / source.DpiX,
                source.PixelHeight * 96d / source.DpiY);
            drawing.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
                null,
                size);
            drawing.DrawImage(source, size);
        }

        composite.Render(visual);
        composite.Freeze();
        return composite;
    }

    private static void Save(BitmapSource bitmap, string fileName)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "artifacts", "visual");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, fileName));
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
}
