using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using CodexUsageBar.App.Controls;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Input;
using CodexUsageBar.Windows.Startup;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class WidgetWindowInteractionTests
{
    [Fact]
    public void ContextMenu_SystemClickClosesOnlyWhenPointIsOutside() => StaTest.Run(() =>
    {
        var monitor = new RecordingSystemMouseButtonMonitor();
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(isEnabled: false),
            () => { },
            systemMouseButtonMonitor: monitor);
        try
        {
            window.Left = 100;
            window.Top = 100;
            window.Show();
            window.UpdateLayout();

            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.PlacementTarget = window.WidgetRoot;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            menu.ApplyTemplate();
            menu.UpdateLayout();

            Assert.True(menu.IsOpen);
            Assert.Equal(1, monitor.StartCount);
            Assert.True(monitor.IsRunning);

            var inside = menu.PointToScreen(new Point(1, 1));
            monitor.RaiseButtonDown(
                checked((int)Math.Round(inside.X)),
                checked((int)Math.Round(inside.Y)));
            ProcessPendingDispatcherWork();
            Assert.True(menu.IsOpen);

            var submenuOwner = menu.Items
                .OfType<MenuItem>()
                .First(item => item.HasItems);
            submenuOwner.IsSubmenuOpen = true;
            submenuOwner.ApplyTemplate();
            ProcessPendingDispatcherWork();
            var submenuPopup = Assert.IsType<RightPreferredPopup>(
                submenuOwner.Template.FindName("PART_Popup", submenuOwner));
            var submenuChild = Assert.IsAssignableFrom<FrameworkElement>(submenuPopup.Child);
            var insideSubmenu = submenuChild.PointToScreen(new Point(1, 13));
            monitor.RaiseButtonDown(
                checked((int)Math.Round(insideSubmenu.X)),
                checked((int)Math.Round(insideSubmenu.Y)));
            ProcessPendingDispatcherWork();
            Assert.True(menu.IsOpen);

            monitor.RaiseButtonDown(-10_000, -10_000);
            ProcessPendingDispatcherWork();
            Assert.False(menu.IsOpen);
            Assert.False(monitor.IsRunning);
            Assert.Equal(1, monitor.StopCount);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void PlacementLayout_AppliesOpticalOffsetOnlyOnTaskbar() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(isEnabled: false),
            () => { });
        try
        {
            window.ApplyPlacementLayout(
                widthDip: 160d,
                useTaskbarOpticalAlignment: true);

            Assert.Equal(new Thickness(2, 0, 0, 0), window.WidgetPanel.Margin);
            Assert.Equal(168d, window.WidgetContentHost.Width);
            Assert.Equal(HorizontalAlignment.Left, window.WidgetContentHost.HorizontalAlignment);
            Assert.Equal(1d, window.WidgetContentOffsetTransform.Y);

            window.ApplyPlacementLayout(
                widthDip: 416d / 3d,
                useTaskbarOpticalAlignment: false);

            Assert.Equal(new Thickness(2, 0, 0, 0), window.WidgetPanel.Margin);
            Assert.Equal(416d / 3d, window.WidgetContentHost.Width, precision: 6);
            Assert.Equal(HorizontalAlignment.Left, window.WidgetContentHost.HorizontalAlignment);
            Assert.Same(Transform.Identity, window.WidgetPanel.RenderTransform);
            Assert.Equal(0d, window.WidgetContentOffsetTransform.Y);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => window.ApplyPlacementWidth(111.99d));
        }
        finally
        {
            window.Close();
        }
    });

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
            Assert.Equal(10, items.Length);
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
                AssertThemeProgressStructure(item);
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
            var placementItem = Assert.IsType<MenuItem>(items[3]);
            Assert.Same(window.PositionSettingsMenuItem, placementItem);
            Assert.Equal("显示位置", placementItem.Header);
            Assert.Empty(placementItem.Items);
            var hideFiveHourItem = Assert.IsType<MenuItem>(items[4]);
            Assert.Equal("隐藏 5 小时", hideFiveHourItem.Header);
            Assert.True(hideFiveHourItem.IsCheckable);
            Assert.Equal("Toggle", hideFiveHourItem.Tag);
            var startupItem = Assert.IsType<MenuItem>(items[5]);
            Assert.Equal("开机启动", startupItem.Header);
            Assert.True(startupItem.IsCheckable);
            Assert.Equal("Toggle", startupItem.Tag);
            Assert.Equal("调试面板", Assert.IsType<MenuItem>(items[6]).Header);
            var aboutItem = Assert.IsType<MenuItem>(items[7]);
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
            Assert.IsType<Separator>(items[8]);
            var exitItem = Assert.IsType<MenuItem>(items[9]);
            Assert.Same(
                window.FindResource("CodexMenuIconStyle"),
                Assert.IsType<Path>(exitItem.Icon).Style);
            Assert.Equal("退出", Assert.IsType<MenuItem>(items[9]).Header);
            Assert.Equal("Exit", Assert.IsType<MenuItem>(items[9]).Tag);
            Assert.Empty(startup.Writes);

            Assert.IsType<MenuItem>(items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            startupItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.IsType<MenuItem>(items[9]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

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
    public void ColorTheme_AppliesStoredChoiceAndPersistsMenuChanges() => StaTest.Run(() =>
    {
        var preferences = new SessionWidgetPreferences(
            colorTheme: QuotaColorTheme.Purple);
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            preferences: preferences);
        try
        {
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

            Assert.False(window.ThemeBlueMenuItem.IsChecked);
            Assert.True(window.ThemePurpleMenuItem.IsChecked);

            window.ThemeForestMenuItem.RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(QuotaColorTheme.Forest, preferences.ColorTheme);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.True(window.ThemeForestMenuItem.IsChecked);
            Assert.False(window.ThemePurpleMenuItem.IsChecked);
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
    public void StartupRegistrationFailure_DisablesItemWithoutCrashingMenu() => StaTest.Run(() =>
    {
        var startup = new FailingStartupRegistration(failRead: true);
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            startup,
            () => { });
        try
        {
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

            Assert.False(window.StartupMenuItem.IsEnabled);
            Assert.False(window.StartupMenuItem.IsChecked);
            Assert.Equal("开机启动（不可用）", window.StartupMenuItem.Header);
            Assert.Equal(
                "Windows 拒绝访问开机启动设置。",
                window.StartupMenuItem.ToolTip);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void StartupWriteFailure_RollsBackToggleAndShowsUnavailableState() => StaTest.Run(() =>
    {
        var startup = new FailingStartupRegistration(failRead: false);
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            startup,
            () => { });
        try
        {
            var menu = Assert.IsType<ContextMenu>(window.WidgetRoot.ContextMenu);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            window.StartupMenuItem.IsChecked = true;

            window.StartupMenuItem.RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.False(window.StartupMenuItem.IsChecked);
            Assert.False(window.StartupMenuItem.IsEnabled);
            Assert.Equal("开机启动（不可用）", window.StartupMenuItem.Header);
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
    public void PlacementPreference_AppliesInDirectPositionPanel() => StaTest.Run(() =>
    {
        var preferences = new SessionWidgetPreferences(
            placementPreference: WidgetPlacementPreference.CodexSidebarPreferred);
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            preferences: preferences);
        try
        {
            window.PositionSettingsMenuItem.RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));
            var settingsWindow = Assert.IsType<Views.PositionSettingsWindow>(
                window.ActivePositionSettingsWindow);
            Assert.True(settingsWindow.PlacementCodexSidebarChoice.IsChecked);

            settingsWindow.PlacementTaskbarChoice.RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(
                WidgetPlacementPreference.TaskbarPreferred,
                preferences.PlacementPreference);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void PositionSettingsWindow_UsesSafeRangesAndPersistsOffsetsIndependently() => StaTest.Run(() =>
    {
        var preferences = new SessionWidgetPreferences(
            placementPreference: WidgetPlacementPreference.CodexSidebarPreferred,
            taskbarHorizontalOffsetDip: 12d,
            codexSidebarHorizontalOffsetDip: -8d);
        var window = new Views.PositionSettingsWindow(
            preferences,
            SystemTheme.Dark);
        var debugWindow = new Views.DebugWindow(
            new DebugViewModel(),
            SystemTheme.Dark);
        var changeCount = 0;
        var placementChangeCount = 0;
        window.HorizontalOffsetsChanged += (_, _) => changeCount++;
        window.PlacementPreferenceChanged += (_, _) => placementChangeCount++;
        try
        {
            Assert.Equal(debugWindow.Width, window.Width);
            Assert.Equal(288d, window.Height);
            Assert.Equal(debugWindow.DebugPanel.Width, window.PositionPanel.Width);
            Assert.Equal(264d, window.PositionPanel.Height);
            Assert.Equal(
                debugWindow.DebugPanel.CornerRadius,
                window.PositionPanel.CornerRadius);
            Assert.Equal(
                debugWindow.DebugPanel.BorderThickness,
                window.PositionPanel.BorderThickness);
            Assert.Equal(
                debugWindow.DebugTitleBar.Padding,
                window.PositionTitleBar.Padding);
            Assert.Equal(new Thickness(6, 0, 6, 10), window.PositionControls.Margin);
            var shadow = Assert.IsType<DropShadowEffect>(window.PositionPanel.Effect);
            var debugShadow = Assert.IsType<DropShadowEffect>(
                debugWindow.DebugPanel.Effect);
            Assert.Equal(debugShadow.BlurRadius, shadow.BlurRadius);
            Assert.Equal(debugShadow.ShadowDepth, shadow.ShadowDepth);
            Assert.False(window.PlacementAutomaticChoice.IsChecked);
            Assert.False(window.PlacementTaskbarChoice.IsChecked);
            Assert.True(window.PlacementCodexSidebarChoice.IsChecked);
            Assert.False(window.PlacementSystemTrayChoice.IsChecked);
            Assert.Same(
                window.FindResource("CodexSliderStyle"),
                window.TaskbarHorizontalOffsetSlider.Style);
            Assert.Same(
                window.FindResource("CodexSliderStyle"),
                window.CodexHorizontalOffsetSlider.Style);
            Assert.Same(
                window.CodexHorizontalOffsetSlider,
                window.PositionControls.Children[4]);
            Assert.Same(
                window.TaskbarHorizontalOffsetSlider,
                window.PositionControls.Children[6]);

            window.PlacementTaskbarChoice.RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(
                WidgetPlacementPreference.TaskbarPreferred,
                preferences.PlacementPreference);
            Assert.True(window.PlacementTaskbarChoice.IsChecked);
            Assert.Equal(1, placementChangeCount);

            window.ApplyHorizontalOffsetRanges(
                new HorizontalOffsetRange(0d, 80d),
                new HorizontalOffsetRange(-40d, 60d));

            Assert.Equal(15d, window.TaskbarHorizontalOffsetSlider.Value, precision: 6);
            Assert.Equal(-20d, window.CodexHorizontalOffsetSlider.Value, precision: 6);
            Assert.Equal(0d, window.TaskbarHorizontalOffsetSlider.Minimum);
            Assert.Equal(-100d, window.CodexHorizontalOffsetSlider.Minimum);

            window.TaskbarHorizontalOffsetSlider.Value = 30d;
            window.CodexHorizontalOffsetSlider.Value = -40d;

            Assert.Equal(24d, preferences.TaskbarHorizontalOffsetDip);
            Assert.Equal(-16d, preferences.CodexSidebarHorizontalOffsetDip);
            Assert.Equal("+24 px", window.TaskbarHorizontalOffsetValueText.Text);
            Assert.Equal("-16 px", window.CodexHorizontalOffsetValueText.Text);
            Assert.Equal(2, changeCount);
        }
        finally
        {
            debugWindow.Close();
            window.Close();
        }
    });

    [Fact]
    public void DisplayPositionMenu_IgnoresHoverAndOpensFullPanelOnClick() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        try
        {
            window.PositionSettingsMenuItem.RaiseEvent(
                new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = Mouse.MouseEnterEvent,
                });
            Assert.Null(window.ActivePositionSettingsWindow);

            window.PositionSettingsMenuItem.RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));

            var settingsWindow = Assert.IsType<Views.PositionSettingsWindow>(
                window.ActivePositionSettingsWindow);
            Assert.True(settingsWindow.IsVisible);
            Assert.Equal("显示位置", window.PositionSettingsMenuItem.Header);
            Assert.Empty(window.PositionSettingsMenuItem.Items);
            Assert.Equal("显示位置", settingsWindow.Title);
            Assert.Equal(4, settingsWindow.PlacementChoices.Children.Count);
            settingsWindow.Close();
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void CodexPositiveOffset_UsesFineControlNearZero() => StaTest.Run(() =>
    {
        var preferences = new SessionWidgetPreferences();
        var window = new Views.PositionSettingsWindow(
            preferences,
            SystemTheme.Dark);
        try
        {
            window.ApplyHorizontalOffsetRanges(
                new HorizontalOffsetRange(0d, 80d),
                new HorizontalOffsetRange(-40d, 400d));

            window.CodexHorizontalOffsetSlider.Value = 25d;

            Assert.Equal(25d, preferences.CodexSidebarHorizontalOffsetDip);
            Assert.Equal("+25 px", window.CodexHorizontalOffsetValueText.Text);
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
    public void TrayIconState_PrefersFiveHourAndFallsBackToWeekly() => StaTest.Run(() =>
    {
        var primaryWindow = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        var fallbackWindow = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            display: new WidgetDisplayModel(
                new QuotaDisplayWindow("--", "5h", "--", 1),
                new QuotaDisplayWindow("41%", "周", "周五 18:20", 1),
                "weekly only",
                IsRefreshing: false,
                IsStale: false));
        try
        {
            Assert.True(primaryWindow.TryCreateTrayIconState(out var primary));
            Assert.Equal(72d, primary.Progress);
            Assert.Equal("72", primary.Text);

            Assert.True(fallbackWindow.TryCreateTrayIconState(out var fallback));
            Assert.Equal(41d, fallback.Progress);
            Assert.Equal("41", fallback.Text);
        }
        finally
        {
            fallbackWindow.Close();
            primaryWindow.Close();
        }
    });

    [Fact]
    public void TrayIconState_UsesFullLabelAtOneHundredPercent() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { },
            display: new WidgetDisplayModel(
                new QuotaDisplayWindow("100%", "5h", "21:00", 1),
                new QuotaDisplayWindow("41%", "周", "周五 18:20", 1),
                "full",
                IsRefreshing: false,
                IsStale: false));
        try
        {
            Assert.True(window.TryCreateTrayIconState(out var state));
            Assert.Equal(100d, state.Progress);
            Assert.Equal("满", state.Text);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TrayIconState_TracksSelectedThemeAndSystemMode() => StaTest.Run(() =>
    {
        var window = CreateWindow(
            new RecordingRefreshRequester(),
            new RecordingStartupRegistration(false),
            () => { });
        var changeCount = 0;
        window.TrayIconStateChanged += (_, _) => changeCount++;
        try
        {
            window.ThemePurpleMenuItem.RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.True(window.TryCreateTrayIconState(out var dark));
            Assert.Equal(Color.FromRgb(0xF8, 0xF7, 0xFC), dark.TextColor);
            Assert.Equal(Color.FromRgb(0x2F, 0x2F, 0x2F), dark.TrackColor);
            Assert.Equal(Color.FromRgb(0xD4, 0xA7, 0xFF), dark.GradientStartColor);
            Assert.Equal(Color.FromRgb(0x9B, 0x6C, 0xFF), dark.GradientMiddleColor);
            Assert.Equal(Color.FromRgb(0x5B, 0x43, 0xFF), dark.GradientEndColor);

            window.ApplySystemTheme(SystemTheme.Light);

            Assert.True(window.TryCreateTrayIconState(out var light));
            Assert.Equal(Color.FromRgb(0x20, 0x21, 0x24), light.TextColor);
            Assert.Equal(Color.FromRgb(0xB6, 0xB6, 0xB6), light.TrackColor);
            Assert.Equal(Color.FromRgb(0xE2, 0xC8, 0xFF), light.GradientStartColor);
            Assert.Equal(Color.FromRgb(0xB8, 0x8C, 0xFF), light.GradientMiddleColor);
            Assert.Equal(Color.FromRgb(0x80, 0x6C, 0xFF), light.GradientEndColor);
            Assert.Equal(2, changeCount);
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
        IWidgetPreferences? preferences = null,
        ISystemMouseButtonMonitor? systemMouseButtonMonitor = null,
        WidgetDisplayModel? display = null)
    {
        display ??= new WidgetDisplayModel(
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
            new DebugViewModel(),
            systemMouseButtonMonitor: systemMouseButtonMonitor);
    }

    private static void ProcessPendingDispatcherWork()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void AssertThemeRing(MenuItem item, params Color[] expectedColors)
    {
        var icon = AssertThemeProgressStructure(item);
        var arc = Assert.IsType<ProgressArc>(icon.Children[1]);
        var brush = Assert.IsType<LinearGradientBrush>(arc.Stroke);
        Assert.Equal(expectedColors, brush.GradientStops.Select(stop => stop.Color).ToArray());
    }

    private static Grid AssertThemeProgressStructure(MenuItem item)
    {
        var icon = Assert.IsType<Grid>(item.Icon);
        Assert.Equal(11.7d, icon.Width);
        Assert.Equal(11.7d, icon.Height);
        Assert.Equal(2, icon.Children.Count);

        var track = Assert.IsType<Ellipse>(icon.Children[0]);
        Assert.Null(track.Fill);
        Assert.Equal(0.675d, track.StrokeThickness);

        var arc = Assert.IsType<ProgressArc>(icon.Children[1]);
        Assert.Equal(75d, arc.Progress);
        Assert.Equal(1.35d, arc.StrokeThickness);
        return icon;
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

    private sealed class RecordingSystemMouseButtonMonitor : ISystemMouseButtonMonitor
    {
        private bool _isRunning;

        public event EventHandler<SystemMouseButtonDownEventArgs>? ButtonDown;

        public bool IsRunning => _isRunning;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool Start()
        {
            if (_isRunning)
            {
                return true;
            }

            _isRunning = true;
            StartCount++;
            return true;
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            StopCount++;
        }

        public void Dispose()
        {
        }

        public void RaiseButtonDown(int screenX, int screenY) =>
            ButtonDown?.Invoke(
                this,
                new SystemMouseButtonDownEventArgs(screenX, screenY));
    }

    private sealed class FailingStartupRegistration(bool failRead) : IStartupRegistration
    {
        public bool IsEnabled => failRead
            ? throw new UnauthorizedAccessException()
            : false;

        public void SetEnabled(bool enabled) =>
            throw new UnauthorizedAccessException();
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

        public QuotaColorTheme ColorTheme { get; set; } = QuotaColorTheme.Blue;

        public WidgetPlacementPreference PlacementPreference { get; set; } =
            WidgetPlacementPreference.Automatic;

        public double TaskbarHorizontalOffsetDip { get; set; }

        public double CodexSidebarHorizontalOffsetDip { get; set; }
    }
}
