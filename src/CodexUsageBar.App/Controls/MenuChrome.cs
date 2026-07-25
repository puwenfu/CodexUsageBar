using System.Windows;
using System.Windows.Media;

namespace CodexUsageBar.App.Controls;

public static class MenuChrome
{
    private static readonly Brush DefaultIconBrush = CreateDefaultIconBrush();

    public static readonly DependencyProperty IsRootItemProperty =
        DependencyProperty.RegisterAttached(
            "IsRootItem",
            typeof(bool),
            typeof(MenuChrome),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsRootItem(DependencyObject element) =>
        (bool)element.GetValue(IsRootItemProperty);

    public static void SetIsRootItem(DependencyObject element, bool value) =>
        element.SetValue(IsRootItemProperty, value);

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.RegisterAttached(
            "IconBrush",
            typeof(Brush),
            typeof(MenuChrome),
            new FrameworkPropertyMetadata(
                DefaultIconBrush,
                FrameworkPropertyMetadataOptions.Inherits));

    public static Brush GetIconBrush(DependencyObject element) =>
        (Brush)element.GetValue(IconBrushProperty);

    public static void SetIconBrush(DependencyObject element, Brush value) =>
        element.SetValue(IconBrushProperty, value);

    private static Brush CreateDefaultIconBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0x9D));
        brush.Freeze();
        return brush;
    }
}
