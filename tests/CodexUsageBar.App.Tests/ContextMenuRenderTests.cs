using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CodexUsageBar.App.Controls;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ContextMenuRenderTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void MenuTemplate_RendersThemeRingAndCompactToggleAtTargetDpi(int dpi) => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/SystemThemeDark.xaml",
                UriKind.Relative),
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        });
        var itemStyle = Assert.IsType<Style>(resources["CodexMenuItemStyle"]);
        var menu = new ContextMenu
        {
            Style = Assert.IsType<Style>(resources["CodexContextMenuStyle"]),
        };
        menu.Resources.MergedDictionaries.Add(resources);
        menu.Resources["QuotaProgressBrush"] = new SolidColorBrush(
            Color.FromRgb(0x58, 0x5E, 0xF6));
        var toggleItem = new MenuItem
        {
            Header = "开机启动",
            Tag = "Toggle",
            IsCheckable = true,
            IsChecked = true,
            Style = itemStyle,
            Icon = CreateLineIcon(Assert.IsAssignableFrom<Geometry>(resources["MenuStartupIcon"])),
        };
        menu.Items.Add(toggleItem);
        var uncheckedToggleItem = new MenuItem
        {
            Header = "隐藏 5 小时",
            Tag = "Toggle",
            IsCheckable = true,
            IsChecked = false,
            Style = itemStyle,
            Icon = CreateLineIcon(Assert.IsAssignableFrom<Geometry>(resources["MenuHideIcon"])),
        };
        menu.Items.Add(uncheckedToggleItem);
        menu.Items.Add(new MenuItem
        {
            Header = "沧海星澜",
            Tag = "Theme",
            IsCheckable = true,
            IsChecked = true,
            Style = itemStyle,
            Icon = CreateThemeProgress(),
        });

        menu.ApplyTemplate();
        foreach (var item in menu.Items.Cast<MenuItem>())
        {
            item.ApplyTemplate();
        }
        var checkedTrack = Assert.IsType<Border>(
            toggleItem.Template.FindName("ToggleSwitch", toggleItem));
        var checkedThumb = Assert.IsType<Ellipse>(
            toggleItem.Template.FindName("ToggleThumb", toggleItem));
        Assert.Equal(Visibility.Visible, checkedTrack.Visibility);
        Assert.Equal(23.8d, checkedTrack.Width);
        Assert.Equal(13.6d, checkedTrack.Height);
        Assert.Equal(Color.FromRgb(0xA8, 0xFF, 0x7A), AssertSolidColor(checkedTrack.Background));
        Assert.Equal(HorizontalAlignment.Right, checkedThumb.HorizontalAlignment);
        Assert.Equal(10d, checkedThumb.Width);

        var uncheckedTrack = Assert.IsType<Border>(
            uncheckedToggleItem.Template.FindName("ToggleSwitch", uncheckedToggleItem));
        var uncheckedThumb = Assert.IsType<Ellipse>(
            uncheckedToggleItem.Template.FindName("ToggleThumb", uncheckedToggleItem));
        Assert.Equal(Visibility.Visible, uncheckedTrack.Visibility);
        Assert.Equal(Color.FromRgb(0x30, 0x30, 0x36), AssertSolidColor(uncheckedTrack.Background));
        Assert.Equal(HorizontalAlignment.Left, uncheckedThumb.HorizontalAlignment);

        menu.Measure(new Size(240, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();

        var bitmap = Render(menu, dpi);
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
        Assert.True(CountBlueThemePixels(bitmap) > 0);
        Assert.True(CountGreenTogglePixels(bitmap) > 0);
        AssertCornersAreSofterThanCenter(bitmap);
    });

    private static Color AssertSolidColor(Brush brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    private static Path CreateLineIcon(Geometry data) => new()
    {
        Width = 13,
        Height = 13,
        Data = data,
        Stroke = Brushes.Gray,
        StrokeThickness = 1.3,
        Stretch = Stretch.Uniform,
    };

    private static Grid CreateThemeProgress()
    {
        var icon = new Grid
        {
            Width = 11.7,
            Height = 11.7,
        };
        icon.Children.Add(new Ellipse
        {
            StrokeThickness = 1.35,
            Stroke = new SolidColorBrush(Color.FromRgb(0x2F, 0x2F, 0x2F)),
        });
        icon.Children.Add(new ProgressArc
        {
            Progress = 85,
            StrokeThickness = 1.35,
            Stroke = new LinearGradientBrush(
                Color.FromRgb(0x8D, 0x9E, 0xFC),
                Color.FromRgb(0x4E, 0x4F, 0xF4),
                45),
        });
        return icon;
    }

    private static RenderTargetBitmap Render(FrameworkElement element, int dpi)
    {
        var scale = dpi / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static int CountBlueThemePixels(RenderTargetBitmap bitmap) =>
        CountPixels(bitmap, (blue, green, red, alpha) =>
            alpha > 0 && blue > 180 && blue > red + 15 && blue > green + 10);

    private static int CountGreenTogglePixels(RenderTargetBitmap bitmap) =>
        CountPixels(bitmap, (blue, green, red, alpha) =>
            alpha > 0 && green > 240 && red > 150 && blue < 150);

    private static int CountPixels(
        RenderTargetBitmap bitmap,
        Func<byte, byte, byte, byte, bool> predicate)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (predicate(
                    pixels[index],
                    pixels[index + 1],
                    pixels[index + 2],
                    pixels[index + 3]))
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertCornersAreSofterThanCenter(RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var cornerAlphaIndexes = new[]
        {
            3,
            ((bitmap.PixelWidth - 1) * 4) + 3,
            ((bitmap.PixelHeight - 1) * stride) + 3,
            ((bitmap.PixelHeight - 1) * stride) + ((bitmap.PixelWidth - 1) * 4) + 3,
        };

        var centerAlphaIndex =
            ((bitmap.PixelHeight / 2) * stride) + ((bitmap.PixelWidth / 2) * 4) + 3;
        var averageCornerAlpha = cornerAlphaIndexes.Average(index => pixels[index]);
        Assert.True(averageCornerAlpha < pixels[centerAlphaIndex]);
    }
}
