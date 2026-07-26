using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Windows.Startup;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class WidgetWindowInteractionTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void ContextMenuClearance_KeepsVisiblePanelOnePhysicalPixelAboveTaskbar(
        double dpiScale)
    {
        const double panelBottomDip = 700d;
        const double workAreaBottomDip = 680d;

        var offset = WidgetWindow.CalculateTaskbarClearanceOffset(
            panelBottomDip,
            workAreaBottomDip,
            1d / dpiScale);

        Assert.Equal(
            (workAreaBottomDip * dpiScale) - 1d,
            (panelBottomDip + offset) * dpiScale,
            precision: 6);
    }

    [Fact]
    public void ContextMenuClearance_DoesNotMovePanelThatAlreadyClearsTaskbar()
    {
        var offset = WidgetWindow.CalculateTaskbarClearanceOffset(
            panelBottomDip: 670d,
            workAreaBottomDip: 680d,
            onePhysicalPixelDip: 1d);

        Assert.Equal(0d, offset);
    }

    [Fact]
    public void ContextMenu_HasExactItems_AndStartupWritesOnlyAfterClick() => StaTest.Run(() =>
    {
        var refresh = new RecordingRefreshRequester();
        var startup = new RecordingStartupRegistration(isEnabled: false);
        var exitCalls = 0;
        var window = CreateWindow(refresh, startup, () => exitCalls++);
        try
        {
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            var items = menu.Items.Cast<object>().ToArray();
            Assert.Same(window.FindResource("CodexContextMenuStyle"), menu.Style);
            Assert.Equal(9, items.Length);
            Assert.Equal("立即刷新", Assert.IsType<MenuItem>(items[0]).Header);
            var themeItem = Assert.IsType<MenuItem>(items[1]);
            Assert.Equal("主题颜色", themeItem.Header);
            Assert.Equal(
                ["沧海星澜", "暮紫流烟", "绯樱流霞", "薄荷清露", "苍林叠翠"],
                themeItem.Items.Cast<MenuItem>().Select(item => item.Header).ToArray());
            Assert.All(
                themeItem.Items.Cast<MenuItem>(),
                item => Assert.Equal(4, Assert.IsType<string>(item.Header).Length));
            Assert.All(themeItem.Items.Cast<MenuItem>(), item => Assert.True(item.IsCheckable));
            Assert.All(themeItem.Items.Cast<MenuItem>(), item =>
            {
                Assert.Equal("Theme", item.Tag);
                Assert.IsType<Ellipse>(item.Icon);
                Assert.Equal(new Thickness(8, 6, 2, 6), item.Padding);
                Assert.Same(window.FindResource("ColorThemeHeaderTemplate"), item.HeaderTemplate);
                var header = Assert.IsType<TextBlock>(item.HeaderTemplate.LoadContent());
                Assert.Equal(new Thickness(2, 0, 0, 0), header.Margin);
            });
            var refreshStyleItem = Assert.IsType<MenuItem>(items[2]);
            Assert.Equal("刷新样式", refreshStyleItem.Header);
            Assert.Equal(
                ["进度环旋转", "流光旋转", "光点巡航"],
                refreshStyleItem.Items.Cast<MenuItem>().Select(item => item.Header).ToArray());
            Assert.All(refreshStyleItem.Items.Cast<MenuItem>(), item => Assert.True(item.IsCheckable));
            var hideFiveHourItem = Assert.IsType<MenuItem>(items[3]);
            Assert.Equal("隐藏 5 小时", hideFiveHourItem.Header);
            Assert.True(hideFiveHourItem.IsCheckable);
            Assert.Equal("Toggle", hideFiveHourItem.Tag);
            var startupItem = Assert.IsType<MenuItem>(items[4]);
            Assert.Equal("开机启动", startupItem.Header);
            Assert.True(startupItem.IsCheckable);
            Assert.Equal("Toggle", startupItem.Tag);
            Assert.Equal("调试面板", Assert.IsType<MenuItem>(items[5]).Header);
            var aboutItem = Assert.IsType<MenuItem>(items[6]);
            Assert.Equal("关于", aboutItem.Header);
            Assert.Single(aboutItem.Items);
            var aboutContent = Assert.IsType<MenuItem>(aboutItem.Items[0]);
            var aboutPanel = Assert.IsType<StackPanel>(aboutContent.Header);
            Assert.Equal(168d, aboutPanel.Width);
            var aboutDescription = Assert.IsType<TextBlock>(aboutPanel.Children[1]);
            Assert.Single(aboutDescription.Inlines.OfType<LineBreak>());
            Assert.Equal(
                "by Wenfu Pu",
                Assert.IsType<TextBlock>(aboutPanel.Children[3]).Text);
            Assert.Equal(
                AboutVersionProvider.GetDisplayText(typeof(WidgetWindow).Assembly),
                window.AboutVersionText.Text);
            Assert.IsType<Separator>(items[7]);
            var exitItem = Assert.IsType<MenuItem>(items[8]);
            Assert.Same(
                window.FindResource("CodexMenuIconStyle"),
                Assert.IsType<Path>(exitItem.Icon).Style);
            Assert.Equal("退出", Assert.IsType<MenuItem>(items[8]).Header);
            Assert.Equal("Exit", Assert.IsType<MenuItem>(items[8]).Tag);
            Assert.Empty(startup.Writes);

            Assert.IsType<MenuItem>(items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            startupItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.IsType<MenuItem>(items[8]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal([RefreshReason.Manual], refresh.Reasons);
            Assert.Equal([true], startup.Writes);
            Assert.Equal(1, exitCalls);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void RefreshAnimationStyle_AppliesAtStartupAndPersistsMenuChanges() => StaTest.Run(() =>
    {
        var preferences = new SessionWidgetPreferences(
            refreshAnimationStyle: RefreshAnimationStyle.ProgressRing);
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            preferences: preferences);
        try
        {
            Assert.Equal(RefreshAnimationStyle.ProgressRing, window.FiveHourMeter.RefreshAnimationStyle);
            Assert.Equal(RefreshAnimationStyle.ProgressRing, window.WeeklyMeter.RefreshAnimationStyle);

            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.True(window.RefreshProgressRingMenuItem.IsChecked);
            Assert.False(window.RefreshHighlightSweepMenuItem.IsChecked);
            Assert.False(window.RefreshDotOrbitMenuItem.IsChecked);

            window.RefreshDotOrbitMenuItem.RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(RefreshAnimationStyle.DotOrbit, preferences.RefreshAnimationStyle);
            Assert.Equal(RefreshAnimationStyle.DotOrbit, window.FiveHourMeter.RefreshAnimationStyle);
            Assert.Equal(RefreshAnimationStyle.DotOrbit, window.WeeklyMeter.RefreshAnimationStyle);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void HideFiveHourPreference_AppliesAtStartupAndMenuOpen() => StaTest.Run(() =>
    {
        var preferences = new SessionWidgetPreferences(hideFiveHourQuota: true);
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            preferences: preferences);
        try
        {
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

            Assert.True(window.HideFiveHourMenuItem.IsChecked);
            Assert.Equal(Visibility.Collapsed, window.FiveHourMeter.Visibility);
            Assert.Equal(Visibility.Collapsed, window.FiveHourResetText.Visibility);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ThemeMenuItems_UseActualGradientRings() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        try
        {
            AssertThemeRing(
                window.ThemeBlueMenuItem,
                Color.FromRgb(0x8D, 0x9E, 0xFC),
                Color.FromRgb(0x58, 0x5E, 0xF6),
                Color.FromRgb(0x4E, 0x4F, 0xF4));
            AssertThemeRing(
                window.ThemePurpleMenuItem,
                Color.FromRgb(0xD4, 0xA7, 0xFF),
                Color.FromRgb(0x9B, 0x6C, 0xFF),
                Color.FromRgb(0x5B, 0x43, 0xFF));
            AssertThemeRing(
                window.ThemeRoseMenuItem,
                Color.FromRgb(0xFF, 0x75, 0x8A),
                Color.FromRgb(0xFF, 0x65, 0x7A),
                Color.FromRgb(0xFF, 0x45, 0x88));
            AssertThemeRing(
                window.ThemeMintMenuItem,
                Color.FromRgb(0xEB, 0xFF, 0xCD),
                Color.FromRgb(0x54, 0xE3, 0xCA),
                Color.FromRgb(0x67, 0x7F, 0xE4));
            AssertThemeRing(
                window.ThemeForestMenuItem,
                Color.FromRgb(0xE0, 0xF8, 0xBA),
                Color.FromRgb(0xD3, 0xE5, 0x2D),
                Color.FromRgb(0x0C, 0x66, 0x51));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ThemeMenuItems_UseBrighterGradientRingsInLightMode() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        try
        {
            window.ApplySystemTheme(SystemTheme.Light);

            AssertThemeRing(
                window.ThemeBlueMenuItem,
                Color.FromRgb(0xAA, 0xB8, 0xFF),
                Color.FromRgb(0x74, 0x7E, 0xFF),
                Color.FromRgb(0x5E, 0x63, 0xFF));
            AssertThemeRing(
                window.ThemePurpleMenuItem,
                Color.FromRgb(0xE2, 0xC8, 0xFF),
                Color.FromRgb(0xB8, 0x8C, 0xFF),
                Color.FromRgb(0x80, 0x6C, 0xFF));
            AssertThemeRing(
                window.ThemeRoseMenuItem,
                Color.FromRgb(0xFF, 0x9C, 0xAF),
                Color.FromRgb(0xFF, 0x7E, 0x96),
                Color.FromRgb(0xFF, 0x63, 0xA3));
            AssertThemeRing(
                window.ThemeMintMenuItem,
                Color.FromRgb(0xF2, 0xFF, 0xDE),
                Color.FromRgb(0x73, 0xEC, 0xD6),
                Color.FromRgb(0x82, 0x96, 0xF0));
            AssertThemeRing(
                window.ThemeForestMenuItem,
                Color.FromRgb(0xEC, 0xFF, 0xCA),
                Color.FromRgb(0xDE, 0xEF, 0x59),
                Color.FromRgb(0x2B, 0x80, 0x69));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ContextMenu_ActionItemsUseLineIcons() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        try
        {
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            var items = menu.Items.Cast<object>().OfType<MenuItem>().ToArray();

            Assert.All(items, item => Assert.Same(window.FindResource("CodexMenuItemStyle"), item.Style));
            Assert.All(items, item => Assert.IsType<Path>(item.Icon));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void HideFiveHourMenu_ClickUpdatesViewBeforePreferenceAndCanRestore() => StaTest.Run(() =>
    {
        WidgetWindow? window = null;
        var preferences = new RecordingWidgetPreferences(
            () => window?.FiveHourMeter.Visibility ?? Visibility.Visible);
        window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            preferences: preferences);
        try
        {
            window.HideFiveHourMenuItem.IsChecked = true;
            window.HideFiveHourMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(Visibility.Collapsed, window.FiveHourMeter.Visibility);
            Assert.Equal([Visibility.Collapsed], preferences.VisibilityWhenWritten);
            Assert.True(preferences.HideFiveHourQuota);

            window.HideFiveHourMenuItem.IsChecked = false;
            window.HideFiveHourMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(Visibility.Visible, window.FiveHourMeter.Visibility);
            Assert.Equal(Visibility.Visible, window.FiveHourResetText.Visibility);
            Assert.False(preferences.HideFiveHourQuota);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void LeftMouseDown_RequestsManualRefreshAndMarksEventHandled() => StaTest.Run(() =>
    {
        var refresh = new RecordingRefreshRequester();
        var window = CreateWindow(refresh, new RecordingStartupRegistration(false), () => { });
        try
        {
            var mouseEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = window.WidgetRoot,
            };

            window.WidgetRoot.RaiseEvent(mouseEvent);

            Assert.True(mouseEvent.Handled);
            Assert.Equal([RefreshReason.Manual], refresh.Reasons);
            Assert.False(window.IsActive);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void RootTooltip_BindsCompleteDisplayTooltip() => StaTest.Run(() =>
    {
        const string tooltip = "5小时恢复：2026-07-23 03:00:00\n每周恢复：2026-07-25 01:00:00\n最后成功查询：2026-07-23 01:00:00";
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            tooltip);
        try
        {
            window.Show();
            window.UpdateLayout();
            var toolTip = Assert.IsType<ToolTip>(window.WidgetRoot.ToolTip);
            Assert.Equal(tooltip, toolTip.Content);
            Assert.Same(window.FindResource("CodexToolTipStyle"), toolTip.Style);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ReplaceTheme_PreservesUnrelatedResourcesAndReplacesPreviousTheme() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        try
        {
            var resources = window.Resources;
            var unrelated = new ResourceDictionary();
            resources.MergedDictionaries.Add(unrelated);
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/CodexUsageBar.App;component/Themes/QuotaTheme.xaml",
                    UriKind.Relative),
            });

            WidgetWindow.ReplaceTheme(resources, "QuotaThemePurple.xaml");

            Assert.Equal(4, resources.MergedDictionaries.Count);
            Assert.EndsWith(
                "/Themes/SystemThemeDark.xaml",
                resources.MergedDictionaries[0].Source?.ToString(),
                StringComparison.Ordinal);
            Assert.EndsWith(
                "/Themes/ContextMenuTheme.xaml",
                resources.MergedDictionaries[1].Source?.ToString(),
                StringComparison.Ordinal);
            Assert.Same(unrelated, resources.MergedDictionaries[2]);
            Assert.EndsWith(
                "/Themes/QuotaThemePurple.xaml",
                resources.MergedDictionaries[3].Source?.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    });

    private static WidgetWindow CreateWindow(
        IRefreshRequester refresh,
        IStartupRegistration startup,
        Action exit,
        string tooltip = "complete tooltip",
        IWidgetPreferences? preferences = null)
    {
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow("72%", "5h", "00:35", 1),
            new QuotaDisplayWindow("41%", "周", "周五 18:20", 1),
            tooltip,
            IsRefreshing: false,
            IsStale: false);
        return new WidgetWindow(
            new WidgetViewModel(display, 36),
            48,
            refresh,
            startup,
            preferences ?? new SessionWidgetPreferences(),
            exit,
            new DebugViewModel());
    }

    private static void AssertThemeRing(MenuItem item, params Color[] expectedColors)
    {
        var ring = Assert.IsType<Ellipse>(item.Icon);
        Assert.Equal(11.7d, ring.Width);
        Assert.Equal(11.7d, ring.Height);
        Assert.Null(ring.Fill);
        Assert.Equal(1.35d, ring.StrokeThickness);
        var brush = Assert.IsType<LinearGradientBrush>(ring.Stroke);
        Assert.Equal(expectedColors, brush.GradientStops.Select(stop => stop.Color).ToArray());
    }

    private sealed class RecordingRefreshRequester : IRefreshRequester
    {
        public List<RefreshReason> Reasons { get; } = [];

        public Task RequestRefresh(RefreshReason reason)
        {
            Reasons.Add(reason);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStartupRegistration(bool isEnabled) : IStartupRegistration
    {
        public bool IsEnabled { get; private set; } = isEnabled;
        public List<bool> Writes { get; } = [];

        public void SetEnabled(bool enabled)
        {
            Writes.Add(enabled);
            IsEnabled = enabled;
        }
    }

    private sealed class RecordingWidgetPreferences(Func<Visibility> readVisibility) : IWidgetPreferences
    {
        private bool _hideFiveHourQuota;

        public List<Visibility> VisibilityWhenWritten { get; } = [];

        public bool HideFiveHourQuota
        {
            get => _hideFiveHourQuota;
            set
            {
                _hideFiveHourQuota = value;
                VisibilityWhenWritten.Add(readVisibility());
            }
        }

        public RefreshAnimationStyle RefreshAnimationStyle { get; set; } =
            RefreshAnimationStyle.ProgressRing;
    }
}
