using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageBar.App.Controls;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ReadmeInterfacePreviewRenderTests
{
    [Theory]
    [InlineData(false, "codex-usage-bar-menu-dark.png")]
    [InlineData(true, "codex-usage-bar-menu-light.png")]
    public void ReadmePreview_RendersWidgetMenuAndThemeSubmenu(
        bool isLight,
        string fileName) => StaTest.Run(() =>
    {
        const int dpi = 150;
        var systemTheme = isLight ? SystemTheme.Light : SystemTheme.Dark;
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow("70%", "5h", "1h 20m", 1),
            new QuotaDisplayWindow("41%", "周", "4d 12h 56m", 1),
            "5小时恢复：1h 20m\n每周恢复：4d 12h 56m",
            IsRefreshing: false,
            IsStale: false);
        var window = new WidgetWindow(
            new WidgetViewModel(display, 36),
            48,
            new DebugViewModel());

        try
        {
            window.ApplySystemTheme(systemTheme);
            WidgetWindow.ReplaceTheme(
                window.Resources,
                WidgetWindow.ResolveThemeResourceName("QuotaTheme.xaml", systemTheme));
            ShowOffscreen(window);

            Assert.IsType<LinearGradientBrush>(window.FindResource("QuotaProgressBrush"));
            var widget = Render(window, dpi);
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.Resources.MergedDictionaries.Add(CreateMenuResources(systemTheme));
            var themeItem = Assert.IsType<MenuItem>(menu.Items[1]);
            window.ThemeBlueMenuItem.IsChecked = true;
            menu.ApplyTemplate();
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                item.ApplyTemplate();
            }

            menu.Measure(new Size(240, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            var submenu = CreateThemeSubmenu(window, systemTheme);
            submenu.ApplyTemplate();
            foreach (var item in submenu.Items.OfType<MenuItem>())
            {
                item.ApplyTemplate();
            }

            var menuBitmap = Render(menu, dpi);
            var submenuBitmap = Render(submenu, dpi);
            var themeItemOrigin = themeItem.TranslatePoint(new Point(), menu);
            var preview = ComposePreview(
                widget,
                menuBitmap,
                submenuBitmap,
                themeItemOrigin.Y,
                systemTheme,
                dpi);

            Assert.True(menuBitmap.PixelWidth > widget.PixelWidth);
            Assert.True(submenuBitmap.PixelWidth > 0);
            Assert.True(submenuBitmap.PixelHeight > 0);
            Assert.Equal(750, preview.PixelWidth);
            Assert.Equal(500, preview.PixelHeight);
            Save(preview, fileName);
        }
        finally
        {
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

    private static ContextMenu CreateThemeSubmenu(
        WidgetWindow window,
        SystemTheme systemTheme)
    {
        var resources = CreateMenuResources(systemTheme);
        var submenu = new ContextMenu
        {
            Style = Assert.IsType<Style>(resources["CodexContextMenuStyle"]),
        };
        submenu.Resources.MergedDictionaries.Add(resources);
        var itemStyle = Assert.IsType<Style>(resources["CodexMenuItemStyle"]);
        var headerTemplate = Assert.IsType<DataTemplate>(resources["ColorThemeHeaderTemplate"]);
        var sourceItems = Assert.IsType<MenuItem>(
            Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu).Items[1])
            .Items
            .Cast<MenuItem>();

        foreach (var source in sourceItems)
        {
            var sourceIcon = Assert.IsType<Grid>(source.Icon);
            var sourceTrack = Assert.Single(
                sourceIcon.Children.OfType<System.Windows.Shapes.Ellipse>());
            var sourceArc = Assert.Single(sourceIcon.Children.OfType<ProgressArc>());
            var icon = new Grid
            {
                Width = sourceIcon.Width,
                Height = sourceIcon.Height,
            };
            icon.Children.Add(new System.Windows.Shapes.Ellipse
            {
                StrokeThickness = sourceTrack.StrokeThickness,
                Stroke = sourceTrack.Stroke,
            });
            icon.Children.Add(new ProgressArc
            {
                Progress = sourceArc.Progress,
                StrokeThickness = sourceArc.StrokeThickness,
                Stroke = sourceArc.Stroke,
            });
            submenu.Items.Add(new MenuItem
            {
                Header = source.Header,
                Tag = source.Tag,
                IsCheckable = true,
                IsChecked = source.IsChecked,
                Padding = source.Padding,
                Style = itemStyle,
                HeaderTemplate = headerTemplate,
                Icon = icon,
            });
        }

        return submenu;
    }

    private static ResourceDictionary CreateMenuResources(SystemTheme systemTheme)
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                $"/CodexUsageBar.App;component/Themes/SystemTheme{systemTheme}.xaml",
                UriKind.Relative),
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        });
        return resources;
    }

    private static RenderTargetBitmap Render(FrameworkElement element, int dpi)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            element.UpdateLayout();
        }

        var scale = dpi / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap ComposePreview(
        BitmapSource widget,
        BitmapSource menu,
        BitmapSource submenu,
        double themeItemOffsetY,
        SystemTheme systemTheme,
        int dpi)
    {
        const double width = 480;
        const double height = 320;
        const double taskbarHeight = 48;
        const double menuChromeMargin = 12;
        const double menuX = 18;
        var menuSize = ToDipSize(menu);
        var submenuSize = ToDipSize(submenu);
        var widgetSize = ToDipSize(widget);
        var taskbarTop = height - taskbarHeight;
        var onePhysicalPixelDip = 96d / dpi;
        var menuY =
            taskbarTop - menuSize.Height + menuChromeMargin - onePhysicalPixelDip;
        var submenuX = menuX + menuSize.Width - menuChromeMargin;
        var submenuY = Math.Max(8, menuY + themeItemOffsetY - 12);
        var visibleMenuBottom = menuY + menuSize.Height - menuChromeMargin;

        Assert.Equal(taskbarTop - onePhysicalPixelDip, visibleMenuBottom, precision: 6);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi / 96d),
            (int)Math.Ceiling(height * dpi / 96d),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var isLight = systemTheme == SystemTheme.Light;
            var desktop = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(
                        isLight ? Color.FromRgb(0xE7, 0xF0, 0xFA) : Color.FromRgb(0x14, 0x14, 0x1A),
                        0),
                    new GradientStop(
                        isLight ? Color.FromRgb(0xF8, 0xF3, 0xF8) : Color.FromRgb(0x28, 0x25, 0x30),
                        1),
                },
            };
            var taskbarBrush = new SolidColorBrush(
                isLight ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x1C, 0x1C, 0x21));
            var taskbarBorder = new Pen(
                new SolidColorBrush(
                    isLight ? Color.FromRgb(0xC9, 0xCC, 0xD2) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                0.7);

            drawing.DrawRectangle(desktop, null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(
                taskbarBrush,
                taskbarBorder,
                new Rect(0, height - taskbarHeight, width, taskbarHeight));
            drawing.DrawImage(menu, new Rect(new Point(menuX, menuY), menuSize));
            drawing.DrawImage(submenu, new Rect(new Point(submenuX, submenuY), submenuSize));
            drawing.DrawImage(
                widget,
                new Rect(
                    new Point(24, height - taskbarHeight),
                    widgetSize));
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Size ToDipSize(BitmapSource bitmap) => new(
        bitmap.PixelWidth * 96d / bitmap.DpiX,
        bitmap.PixelHeight * 96d / bitmap.DpiY);

    private static void Save(BitmapSource bitmap, string fileName)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "assets");
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
