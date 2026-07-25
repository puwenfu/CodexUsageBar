using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexUsageBar.App.Controls;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ContextMenuThemeTests
{
    [Fact]
    public void ContextMenuTheme_ExposesReferenceStylesAndColors() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };

        Assert.Equal(typeof(ContextMenu), Assert.IsType<Style>(resources["CodexContextMenuStyle"]).TargetType);
        Assert.Equal(typeof(MenuItem), Assert.IsType<Style>(resources["CodexMenuItemStyle"]).TargetType);
        Assert.Equal(typeof(Separator), Assert.IsType<Style>(resources["CodexSeparatorStyle"]).TargetType);
        var menuFont = Assert.IsType<FontFamily>(resources["ContextMenuFontFamily"]);
        Assert.Equal("Microsoft YaHei UI", menuFont.Source);
        Assert.All(
            new[] { "CodexContextMenuStyle", "CodexMenuItemStyle", "CodexAboutItemStyle" },
            key => Assert.Contains(
                Assert.IsType<Style>(resources[key]).Setters.OfType<Setter>(),
                setter => setter.Property == Control.FontFamilyProperty &&
                          ReferenceEquals(setter.Value, menuFont)));
        Assert.Equal(
            Color.FromRgb(0x43, 0x43, 0x43),
            Assert.IsType<SolidColorBrush>(resources["ContextMenuBorderBrush"]).Color);
        Assert.Equal(
            Color.FromRgb(0x3B, 0x3B, 0x3B),
            Assert.IsType<SolidColorBrush>(resources["ContextMenuHoverBrush"]).Color);
        Assert.Equal(
            Color.FromRgb(0x35, 0x35, 0x35),
            Assert.IsType<SolidColorBrush>(resources["ContextMenuDividerBrush"]).Color);
        Assert.Equal(
            Color.FromRgb(0xF2, 0xF2, 0xF2),
            Assert.IsType<SolidColorBrush>(resources["ContextMenuPrimaryTextBrush"]).Color);
        Assert.Equal(
            Color.FromRgb(0x9D, 0x9D, 0x9D),
            Assert.IsType<SolidColorBrush>(resources["ContextMenuSecondaryTextBrush"]).Color);
        Assert.Equal(new Thickness(8, 6, 6, 6), resources["ContextMenuItemPadding"]);
        Assert.Equal(new Thickness(8, 6, 2, 6), resources["ContextSubmenuItemPadding"]);
        Assert.Equal(new Thickness(2), resources["ContextMenuOuterPadding"]);
        Assert.Equal(new Thickness(1, 1, 1, 1), resources["ContextMenuItemMargin"]);
        Assert.Equal(new GridLength(18), resources["ContextMenuIconColumnWidth"]);
        Assert.Equal(180d, resources["ContextMenuMinWidth"]);
        Assert.Equal(116d, resources["ContextSubmenuMinWidth"]);
    });

    [Fact]
    public void SubmenuPopup_PrefersRightIndependentlyOfSystemMenuAlignment() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };
        var item = new MenuItem
        {
            Header = "主题颜色",
            Style = Assert.IsType<Style>(resources["CodexMenuItemStyle"]),
        };
        item.Items.Add(new MenuItem { Header = "星海蓝" });

        item.ApplyTemplate();

        var popup = Assert.IsType<RightPreferredPopup>(
            item.Template.FindName("PART_Popup", item));
        Assert.Equal(PlacementMode.Custom, popup.Placement);
        Assert.Same(item, popup.PlacementTarget);
        Assert.True(popup.StaysOpen);
        var callback = Assert.IsType<CustomPopupPlacementCallback>(
            popup.CustomPopupPlacementCallback);
        var placements = callback(
            new Size(120, 96),
            new Size(180, 32),
            new Point());

        Assert.Collection(
            placements,
            right =>
            {
                Assert.Equal(new Point(180, 0), right.Point);
                Assert.Equal(PopupPrimaryAxis.Vertical, right.PrimaryAxis);
            },
            left =>
            {
                Assert.Equal(new Point(-120, 0), left.Point);
                Assert.Equal(PopupPrimaryAxis.Vertical, left.PrimaryAxis);
            });
    });

    [Fact]
    public void ContextMenuChrome_UsesSymmetricEffectSafeArea() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };
        var menu = new ContextMenu
        {
            Style = Assert.IsType<Style>(resources["CodexContextMenuStyle"]),
        };
        menu.Items.Add(new MenuItem { Header = "立即刷新" });

        menu.ApplyTemplate();

        var panel = Assert.IsType<Border>(menu.Template.FindName("ContextMenuPanel", menu));
        var contentHost = Assert.IsType<Border>(
            menu.Template.FindName("ContextMenuContentHost", menu));
        Assert.Equal(new Thickness(12), panel.Margin);
        Assert.Equal(new Thickness(12), contentHost.Margin);
        Assert.Equal(new Thickness(2), contentHost.Padding);
    });

    [Fact]
    public void MenuPanels_UseTheSameSingleSurfaceAsDebugPanel() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };
        var expectedSurface = Assert.IsType<LinearGradientBrush>(
            resources["ContextMenuSurfaceBrush"]);
        var menu = new ContextMenu
        {
            Style = Assert.IsType<Style>(resources["CodexContextMenuStyle"]),
        };
        menu.Items.Add(new MenuItem { Header = "立即刷新" });

        menu.ApplyTemplate();

        var rootPanel = Assert.IsType<Border>(
            menu.Template.FindName("ContextMenuPanel", menu));
        Assert.Same(expectedSurface, rootPanel.Background);

        var item = new MenuItem
        {
            Header = "主题颜色",
            Style = Assert.IsType<Style>(resources["CodexMenuItemStyle"]),
        };
        item.Items.Add(new MenuItem { Header = "星海蓝" });

        item.ApplyTemplate();

        var submenuSurface = Assert.IsType<Border>(
            item.Template.FindName("SubmenuSurface", item));
        Assert.Same(expectedSurface, submenuSurface.Background);
    });

    [Fact]
    public void MainMenuChrome_AlignsRootAndChildItems() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };
        var itemStyle = Assert.IsType<Style>(resources["CodexMenuItemStyle"]);
        var rootItem = new MenuItem
        {
            Header = "主题颜色",
            Style = itemStyle,
        };
        MenuChrome.SetIsRootItem(rootItem, true);
        rootItem.Items.Add(new MenuItem
        {
            Header = "星海蓝",
            Style = itemStyle,
        });
        var menu = new ContextMenu
        {
            Style = Assert.IsType<Style>(resources["CodexContextMenuStyle"]),
        };
        menu.Items.Add(rootItem);

        menu.ApplyTemplate();
        rootItem.ApplyTemplate();
        var childItem = Assert.IsType<MenuItem>(rootItem.Items[0]);
        childItem.ApplyTemplate();
        menu.Measure(new Size(240, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();

        var rootBackground = Assert.IsType<Border>(
            rootItem.Template.FindName("ItemBackground", rootItem));
        var rootContent = Assert.IsType<Border>(
            rootItem.Template.FindName("ItemContentHost", rootItem));
        var childBackground = Assert.IsType<Border>(
            childItem.Template.FindName("ItemBackground", childItem));
        Assert.Equal(new Thickness(1), rootBackground.Margin);
        Assert.Equal(new Thickness(1), rootContent.Margin);
        Assert.Equal(new Thickness(1), childBackground.Margin);
        Assert.Equal(
            new Thickness(18, 4, 6, 4),
            Assert.IsType<Style>(resources["CodexSeparatorStyle"])
                .Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == FrameworkElement.MarginProperty)
                .Value);
    });

    [Fact]
    public void MenuIcons_HaveCompleteSemanticSetAndBalancedStroke() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };
        var keys = new[]
        {
            "MenuRefreshIcon",
            "MenuThemeIcon",
            "MenuHideIcon",
            "MenuStartupIcon",
            "MenuDebugIcon",
            "MenuAboutIcon",
            "MenuExitIcon",
        };

        Assert.All(keys, key => Assert.IsAssignableFrom<Geometry>(resources[key]));
        var style = Assert.IsType<Style>(resources["CodexMenuIconStyle"]);
        Assert.Contains(
            style.Setters.OfType<Setter>(),
            setter => setter.Property == FrameworkElement.WidthProperty &&
                      Equals(setter.Value, 11.2d));
        Assert.Contains(
            style.Setters.OfType<Setter>(),
            setter => setter.Property == FrameworkElement.HeightProperty &&
                      Equals(setter.Value, 11.2d));
        Assert.Contains(
            style.Setters.OfType<Setter>(),
            setter => setter.Property == Shape.StrokeThicknessProperty &&
                      Equals(setter.Value, 1.08d));
        Assert.IsType<Binding>(
            style.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Shape.StrokeProperty)
                .Value);
    });

    [Fact]
    public void MenuIconStroke_FollowsMenuItemForeground() => StaTest.Run(() =>
    {
        Application.ResourceAssembly ??= typeof(WidgetWindow).Assembly;
        var resources = new ResourceDictionary
        {
            Source = new Uri(
                "/CodexUsageBar.App;component/Themes/ContextMenuTheme.xaml",
                UriKind.Relative),
        };
        var icon = new Path
        {
            Style = Assert.IsType<Style>(resources["CodexMenuIconStyle"]),
        };
        var presenter = new ContentPresenter
        {
            Content = icon,
        };
        MenuChrome.SetIconBrush(presenter, Brushes.HotPink);

        presenter.ApplyTemplate();
        presenter.Measure(new Size(40, 40));
        presenter.Arrange(new Rect(0, 0, 40, 40));
        presenter.UpdateLayout();

        Assert.Same(Brushes.HotPink, icon.Stroke);
    });
}
