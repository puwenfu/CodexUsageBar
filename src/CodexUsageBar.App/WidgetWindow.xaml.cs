using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageBar.App.Controls;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Input;
using CodexUsageBar.Windows.Startup;
using CodexUsageBar.Windows.Tray;

namespace CodexUsageBar.App;

public partial class WidgetWindow : Window
{
    internal const double FullTaskbarContentWidthDip = 168d;
    internal const double CodexSidebarContentWidthDip = 416d / 3d;
    private const string StartupMenuHeader = "开机启动";
    private const string StartupUnavailableMenuHeader = "开机启动（不可用）";
    private readonly IRefreshRequester _refreshRequester;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IWidgetPreferences _preferences;
    private readonly Action _exit;
    private readonly DebugViewModel _debugViewModel;
    private readonly ISystemThemeWatcher? _systemThemeWatcher;
    private readonly ISystemMouseButtonMonitor? _systemMouseButtonMonitor;
    private readonly WidgetViewModel _viewModel;
    private SystemTheme _currentSystemTheme = SystemTheme.Dark;
    private string _currentTheme;
    private Views.DebugWindow? _debugWindow;
    private Views.PositionSettingsWindow? _positionSettingsWindow;
    private HorizontalOffsetRange? _taskbarHorizontalOffsetRange;
    private HorizontalOffsetRange? _codexHorizontalOffsetRange;

    internal event EventHandler<bool>? ContextMenuActivityChanged;

    internal event EventHandler? PlacementPreferenceChanged;

    internal event EventHandler? HorizontalOffsetsChanged;

    internal event EventHandler? TrayIconStateChanged;

    internal Views.PositionSettingsWindow? ActivePositionSettingsWindow =>
        _positionSettingsWindow;

    public WidgetWindow(WidgetViewModel viewModel, double taskbarHeightDip, DebugViewModel debugViewModel)
        : this(
            viewModel,
            taskbarHeightDip,
            NullRefreshRequester.Instance,
            NullStartupRegistration.Instance,
            new SessionWidgetPreferences(),
            () => { },
            debugViewModel,
            systemThemeWatcher: null)
    {
    }

    internal WidgetWindow(
        WidgetViewModel viewModel,
        double taskbarHeightDip,
        IRefreshRequester refreshRequester,
        IStartupRegistration startupRegistration,
        IWidgetPreferences preferences,
        Action exit,
        DebugViewModel debugViewModel,
        ISystemThemeWatcher? systemThemeWatcher = null,
        ISystemMouseButtonMonitor? systemMouseButtonMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _refreshRequester = refreshRequester ?? throw new ArgumentNullException(nameof(refreshRequester));
        _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _debugViewModel = debugViewModel ?? throw new ArgumentNullException(nameof(debugViewModel));
        _systemThemeWatcher = systemThemeWatcher;
        _systemMouseButtonMonitor = systemMouseButtonMonitor;
        _currentTheme = GetThemeResourceName(_preferences.ColorTheme);
        if (!double.IsFinite(taskbarHeightDip) || taskbarHeightDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskbarHeightDip));
        }

        InitializeComponent();
        ReplaceTheme(
            Resources,
            ResolveThemeResourceName(_currentTheme, _currentSystemTheme));
        Closed += OnClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_systemMouseButtonMonitor is not null)
        {
            _systemMouseButtonMonitor.ButtonDown += OnSystemMouseButtonDown;
        }
        if (_systemThemeWatcher is not null)
        {
            _systemThemeWatcher.ThemeChanged += OnSystemThemeChanged;
            ApplySystemTheme(_systemThemeWatcher.CurrentTheme);
        }
        else if (Application.Current is { } application)
        {
            ReplaceTheme(
                application.Resources,
                ResolveThemeResourceName(_currentTheme, _currentSystemTheme));
        }
        AboutVersionText.Text = AboutVersionProvider.GetDisplayText(typeof(WidgetWindow).Assembly);
        WidgetToolTip.DataContext = viewModel;
        DataContext = viewModel;
        ApplyFiveHourVisibility(_preferences.HideFiveHourQuota);
        ApplyRefreshAnimationStyle(_preferences.RefreshAnimationStyle);
        Height = taskbarHeightDip;
        MaxHeight = taskbarHeightDip;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _ = _refreshRequester.RequestRefresh(RefreshReason.Manual);
    }

    private void OnSystemThemeChanged(object? sender, SystemThemeChangedEventArgs eventArgs)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplySystemTheme(eventArgs.Theme);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => ApplySystemTheme(eventArgs.Theme));
    }

    internal void ApplySystemTheme(SystemTheme theme)
    {
        _currentSystemTheme = theme;
        SystemThemeResources.Replace(Resources, theme);
        ReplaceTheme(
            Resources,
            ResolveThemeResourceName(_currentTheme, theme));
        ApplyThemePreviewBrushes();
        if (Application.Current is { } application)
        {
            SystemThemeResources.Replace(application.Resources, theme);
            ReplaceTheme(
                application.Resources,
                ResolveThemeResourceName(_currentTheme, theme));
        }

        _debugWindow?.ApplySystemTheme(theme);
        _positionSettingsWindow?.ApplySystemTheme(theme);
        TrayIconStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool TryCreateTrayIconState(out SystemTrayIconState state)
    {
        state = default;
        if (TryFindResource("QuotaProgressBrush") is not LinearGradientBrush progressBrush ||
            TryFindResource("QuotaTrackBrush") is not SolidColorBrush trackBrush ||
            TryFindResource("QuotaPrimaryTextBrush") is not SolidColorBrush textBrush)
        {
            return false;
        }

        var stops = progressBrush.GradientStops
            .OrderBy(stop => stop.Offset)
            .ToArray();
        if (stops.Length < 2)
        {
            return false;
        }

        var quota = HasQuota(_viewModel.FiveHour)
            ? _viewModel.FiveHour
            : HasQuota(_viewModel.Weekly)
                ? _viewModel.Weekly
                : null;
        var middle = stops
            .OrderBy(stop => Math.Abs(stop.Offset - 0.52d))
            .First();
        state = new SystemTrayIconState(
            quota?.Progress ?? 0d,
            quota is null
                ? "--"
                : CreateTrayIconText(quota),
            textBrush.Color,
            trackBrush.Color,
            stops[0].Color,
            middle.Color,
            stops[^1].Color);
        return true;
    }

    private static bool HasQuota(QuotaMeterViewModel quota) =>
        quota.PercentageText.EndsWith('%');

    private static string CreateTrayIconText(QuotaMeterViewModel quota) =>
        quota.Progress >= 99.5d
            ? "满"
            : quota.PercentageText.TrimEnd('%').Trim();

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(WidgetViewModel.FiveHour) or
            nameof(WidgetViewModel.Weekly))
        {
            TrayIconStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyThemePreviewBrushes()
    {
        SetThemePreviewBrush(ThemeBlueMenuItem, "ThemeBluePreviewBrush");
        SetThemePreviewBrush(ThemePurpleMenuItem, "ThemePurplePreviewBrush");
        SetThemePreviewBrush(ThemeRoseMenuItem, "ThemeRosePreviewBrush");
        SetThemePreviewBrush(ThemeMintMenuItem, "ThemeMintPreviewBrush");
        SetThemePreviewBrush(ThemeForestMenuItem, "ThemeForestPreviewBrush");
    }

    private void SetThemePreviewBrush(MenuItem menuItem, string resourceKey)
    {
        if (menuItem.Icon is Grid icon
            && icon.Children.OfType<ProgressArc>().SingleOrDefault() is { } arc
            && FindResource(resourceKey) is System.Windows.Media.Brush brush)
        {
            arc.Stroke = brush;
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_systemMouseButtonMonitor is not null)
        {
            _systemMouseButtonMonitor.ButtonDown -= OnSystemMouseButtonDown;
            _systemMouseButtonMonitor.Dispose();
        }

        if (_systemThemeWatcher is not null)
        {
            _systemThemeWatcher.ThemeChanged -= OnSystemThemeChanged;
            _systemThemeWatcher.Dispose();
        }
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs eventArgs)
    {
        WidgetToolTip.IsOpen = false;
        UpdateStartupMenuState();
        HideFiveHourMenuItem.IsChecked = _preferences.HideFiveHourQuota;
        ThemeBlueMenuItem.IsChecked = _currentTheme == "QuotaTheme.xaml";
        ThemePurpleMenuItem.IsChecked = _currentTheme == "QuotaThemePurple.xaml";
        ThemeRoseMenuItem.IsChecked = _currentTheme == "QuotaThemeRose.xaml";
        ThemeMintMenuItem.IsChecked = _currentTheme == "QuotaThemeMint.xaml";
        ThemeForestMenuItem.IsChecked = _currentTheme == "QuotaThemeForest.xaml";
        RefreshProgressRingMenuItem.IsChecked =
            _preferences.RefreshAnimationStyle == RefreshAnimationStyle.ProgressRing;
        RefreshHighlightSweepMenuItem.IsChecked =
            _preferences.RefreshAnimationStyle == RefreshAnimationStyle.HighlightSweep;
        RefreshDotOrbitMenuItem.IsChecked =
            _preferences.RefreshAnimationStyle == RefreshAnimationStyle.DotOrbit;
        ContextMenuActivityChanged?.Invoke(this, true);

        if (sender is ContextMenu menu)
        {
            _ = _systemMouseButtonMonitor?.Start();
            menu.VerticalOffset = 0d;
            _ = menu.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                () => KeepContextMenuOnePhysicalPixelAboveTaskbar(menu));
        }
    }

    private void OnContextMenuClosed(object? sender, RoutedEventArgs eventArgs)
    {
        _systemMouseButtonMonitor?.Stop();
        ContextMenuActivityChanged?.Invoke(this, false);
    }

    private void OnSystemMouseButtonDown(
        object? sender,
        SystemMouseButtonDownEventArgs eventArgs)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () => DismissContextMenuIfOutside(eventArgs.ScreenX, eventArgs.ScreenY));
    }

    internal void DismissContextMenuIfOutside(int screenX, int screenY)
    {
        if (WidgetRoot.ContextMenu is not { IsOpen: true } menu)
        {
            return;
        }

        var screenPoint = new Point(screenX, screenY);
        if (!ContainsScreenPoint(menu, screenPoint) &&
            !ContainsScreenPointInOpenSubmenu(menu, screenPoint))
        {
            _systemMouseButtonMonitor?.Stop();
            menu.IsOpen = false;
        }
    }

    private static bool ContainsScreenPointInOpenSubmenu(
        ItemsControl owner,
        Point screenPoint)
    {
        foreach (var item in owner.Items.OfType<MenuItem>())
        {
            if (!item.IsSubmenuOpen)
            {
                continue;
            }

            item.ApplyTemplate();
            if (item.Template.FindName("PART_Popup", item) is Popup
                {
                    IsOpen: true,
                    Child: FrameworkElement popupChild,
                } &&
                ContainsScreenPoint(popupChild, screenPoint))
            {
                return true;
            }

            if (ContainsScreenPointInOpenSubmenu(item, screenPoint))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsScreenPoint(
        FrameworkElement element,
        Point screenPoint)
    {
        if (!element.IsVisible ||
            element.ActualWidth <= 0 ||
            element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var topLeft = element.PointToScreen(new Point(0, 0));
            return new Rect(
                topLeft,
                new Size(element.ActualWidth, element.ActualHeight)).Contains(screenPoint);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void KeepContextMenuOnePhysicalPixelAboveTaskbar(ContextMenu menu)
    {
        if (!menu.IsOpen)
        {
            return;
        }

        menu.ApplyTemplate();
        if (menu.Template.FindName("ContextMenuPanel", menu) is not FrameworkElement panel ||
            panel.ActualHeight <= 0 ||
            PresentationSource.FromVisual(panel)?.CompositionTarget is not { } compositionTarget)
        {
            return;
        }

        var transformFromDevice = compositionTarget.TransformFromDevice;
        var panelBottomPixels = panel.PointToScreen(new Point(0, panel.ActualHeight));
        var panelBottomDip = transformFromDevice.Transform(panelBottomPixels).Y;
        var onePhysicalPixelDip = Math.Abs(transformFromDevice.M22);
        if (!double.IsFinite(onePhysicalPixelDip) || onePhysicalPixelDip <= 0)
        {
            onePhysicalPixelDip = 1d;
        }

        menu.VerticalOffset += CalculateTaskbarClearanceOffset(
            panelBottomDip,
            SystemParameters.WorkArea.Bottom,
            onePhysicalPixelDip);
    }

    internal static double CalculateTaskbarClearanceOffset(
        double panelBottomDip,
        double workAreaBottomDip,
        double onePhysicalPixelDip)
    {
        var maximumPanelBottom = workAreaBottomDip - onePhysicalPixelDip;
        return Math.Min(0d, maximumPanelBottom - panelBottomDip);
    }

    private void OnRefreshMenuClick(object sender, RoutedEventArgs eventArgs) =>
        _ = _refreshRequester.RequestRefresh(RefreshReason.Manual);

    private void OnThemeBlueClick(object sender, RoutedEventArgs eventArgs) =>
        SetTheme(QuotaColorTheme.Blue);

    private void OnThemePurpleClick(object sender, RoutedEventArgs eventArgs) =>
        SetTheme(QuotaColorTheme.Purple);

    private void OnThemeRoseClick(object sender, RoutedEventArgs eventArgs) =>
        SetTheme(QuotaColorTheme.Rose);

    private void OnThemeMintClick(object sender, RoutedEventArgs eventArgs) =>
        SetTheme(QuotaColorTheme.Mint);

    private void OnThemeForestClick(object sender, RoutedEventArgs eventArgs) =>
        SetTheme(QuotaColorTheme.Forest);

    private void OnRefreshProgressRingClick(object sender, RoutedEventArgs eventArgs) =>
        SetRefreshAnimationStyle(RefreshAnimationStyle.ProgressRing);

    private void OnRefreshHighlightSweepClick(object sender, RoutedEventArgs eventArgs) =>
        SetRefreshAnimationStyle(RefreshAnimationStyle.HighlightSweep);

    private void OnRefreshDotOrbitClick(object sender, RoutedEventArgs eventArgs) =>
        SetRefreshAnimationStyle(RefreshAnimationStyle.DotOrbit);

    internal void ApplyHorizontalOffsetRanges(
        HorizontalOffsetRange? taskbarRange,
        HorizontalOffsetRange? codexSidebarRange)
    {
        _taskbarHorizontalOffsetRange = taskbarRange;
        _codexHorizontalOffsetRange = codexSidebarRange;
        _positionSettingsWindow?.ApplyHorizontalOffsetRanges(
            taskbarRange,
            codexSidebarRange);
    }

    internal bool OpenContextMenuAtCursor() => OpenContextMenuAt(anchor: null);

    internal bool OpenContextMenuAt(SystemTrayMenuAnchor? anchor)
    {
        if (WidgetRoot.ContextMenu is not { } menu)
        {
            return false;
        }

        if (menu.IsOpen)
        {
            return true;
        }

        menu.PlacementTarget = this;
        if (anchor is { } screenAnchor &&
            PresentationSource.FromVisual(this)?.CompositionTarget is { } compositionTarget)
        {
            var anchorDip = compositionTarget.TransformFromDevice.Transform(
                new Point(screenAnchor.ScreenX, screenAnchor.ScreenY));
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.PlacementRectangle = new Rect(anchorDip, new Size(0d, 0d));
        }
        else
        {
            menu.Placement = PlacementMode.MousePoint;
            menu.PlacementRectangle = Rect.Empty;
        }
        menu.Closed -= OnContextMenuClosed;
        menu.Closed += OnContextMenuClosed;
        menu.IsOpen = true;
        return menu.IsOpen;
    }

    internal void ApplyPlacementWidth(double widthDip)
    {
        const double minimumStableWidthDip = 112d;
        if (!double.IsFinite(widthDip) || widthDip < minimumStableWidthDip)
        {
            throw new ArgumentOutOfRangeException(nameof(widthDip));
        }

        WidgetPanel.ClearValue(WidthProperty);
        WidgetPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        WidgetPanel.Margin = new Thickness(2, 0, 0, 0);
        WidgetPanel.LayoutTransform = Transform.Identity;
        WidgetPanel.RenderTransform = Transform.Identity;
        WidgetPanel.RenderTransformOrigin = new Point(0, 0);
        WidgetContentHost.ClearValue(WidthProperty);
        WidgetContentHost.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    internal void ApplyPlacementLayout(
        double widthDip,
        bool useTaskbarOpticalAlignment)
    {
        ApplyPlacementWidth(widthDip);
        WidgetContentHost.Width = useTaskbarOpticalAlignment
            ? FullTaskbarContentWidthDip
            : CodexSidebarContentWidthDip;
        WidgetContentHost.HorizontalAlignment = HorizontalAlignment.Left;

        WidgetContentOffsetTransform.Y = useTaskbarOpticalAlignment ? 1d : 0d;
    }

    private void SetRefreshAnimationStyle(RefreshAnimationStyle style)
    {
        ApplyRefreshAnimationStyle(style);
        _preferences.RefreshAnimationStyle = style;
    }

    private void ApplyRefreshAnimationStyle(RefreshAnimationStyle style)
    {
        FiveHourMeter.RefreshAnimationStyle = style;
        WeeklyMeter.RefreshAnimationStyle = style;
    }

    private void OnHideFiveHourClick(object sender, RoutedEventArgs eventArgs)
    {
        var hide = HideFiveHourMenuItem.IsChecked;
        ApplyFiveHourVisibility(hide);
        _preferences.HideFiveHourQuota = hide;
    }

    private void ApplyFiveHourVisibility(bool hide)
    {
        var visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        FiveHourMeter.Visibility = visibility;
        FiveHourResetText.Visibility = visibility;
    }

    private void SetTheme(QuotaColorTheme theme)
    {
        var themeName = GetThemeResourceName(theme);
        _currentTheme = themeName;
        _preferences.ColorTheme = theme;
        var resourceName = ResolveThemeResourceName(themeName, _currentSystemTheme);
        ReplaceTheme(Resources, resourceName);
        if (Application.Current is { } application &&
            !application.Resources.MergedDictionaries.Any(
                dictionary => dictionary.Source?.ToString().Contains(resourceName) == true))
        {
            ReplaceTheme(application.Resources, resourceName);
        }

        TrayIconStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static string GetThemeResourceName(QuotaColorTheme theme) =>
        theme switch
        {
            QuotaColorTheme.Blue => "QuotaTheme.xaml",
            QuotaColorTheme.Purple => "QuotaThemePurple.xaml",
            QuotaColorTheme.Rose => "QuotaThemeRose.xaml",
            QuotaColorTheme.Mint => "QuotaThemeMint.xaml",
            QuotaColorTheme.Forest => "QuotaThemeForest.xaml",
            _ => throw new ArgumentOutOfRangeException(nameof(theme)),
        };

    internal static string ResolveThemeResourceName(
        string themeName,
        SystemTheme systemTheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeName);
        if (systemTheme == SystemTheme.Dark)
        {
            return themeName;
        }

        const string extension = ".xaml";
        if (!themeName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Theme resource names must end with .xaml.", nameof(themeName));
        }

        return $"{themeName[..^extension.Length]}Light{extension}";
    }

    internal static void ReplaceTheme(ResourceDictionary resources, string themeName)
    {
        var uri = new Uri($"/CodexUsageBar.App;component/Themes/{themeName}", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source?.ToString().Contains("QuotaTheme") == true)
            {
                merged.RemoveAt(i);
            }
        }
        merged.Add(dict);
    }

    private void OnStartupMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryGetStartupEnabled(out var wasEnabled))
        {
            ShowStartupUnavailable();
            return;
        }

        var enabled = !wasEnabled;
        try
        {
            _startupRegistration.SetEnabled(enabled);
            StartupMenuItem.IsChecked = enabled;
        }
        catch (Exception exception) when (IsStartupRegistrationFailure(exception))
        {
            StartupMenuItem.IsChecked = wasEnabled;
            ShowStartupUnavailable();
        }
    }

    private void UpdateStartupMenuState()
    {
        if (TryGetStartupEnabled(out var enabled))
        {
            StartupMenuItem.Header = StartupMenuHeader;
            StartupMenuItem.IsEnabled = true;
            StartupMenuItem.ToolTip = null;
            StartupMenuItem.IsChecked = enabled;
            return;
        }

        ShowStartupUnavailable();
    }

    private bool TryGetStartupEnabled(out bool enabled)
    {
        try
        {
            enabled = _startupRegistration.IsEnabled;
            return true;
        }
        catch (Exception exception) when (IsStartupRegistrationFailure(exception))
        {
            enabled = false;
            return false;
        }
    }

    private void ShowStartupUnavailable()
    {
        StartupMenuItem.Header = StartupUnavailableMenuHeader;
        StartupMenuItem.IsEnabled = false;
        StartupMenuItem.IsChecked = false;
        StartupMenuItem.ToolTip = "Windows 拒绝访问开机启动设置。";
    }

    private static bool IsStartupRegistrationFailure(Exception exception) =>
        exception is IOException or SecurityException or UnauthorizedAccessException;

    private void OnDebugMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_debugWindow == null)
        {
            _debugWindow = new Views.DebugWindow(_debugViewModel, _currentSystemTheme);
            _debugWindow.Closed += (s, e) => _debugWindow = null;
            PositionDebugWindow();
            CloseContextMenu();
            _debugWindow.Show();
        }
        else
        {
            if (_debugWindow.WindowState == WindowState.Minimized)
                _debugWindow.WindowState = WindowState.Normal;
            PositionDebugWindow();
            CloseContextMenu();
            _debugWindow.Activate();
        }
    }

    private void OnPositionSettingsClick(object sender, RoutedEventArgs eventArgs) =>
        OpenPositionSettingsPanel();

    private void OpenPositionSettingsPanel()
    {
        if (_positionSettingsWindow == null)
        {
            _positionSettingsWindow = new Views.PositionSettingsWindow(
                _preferences,
                _currentSystemTheme);
            _positionSettingsWindow.PlacementPreferenceChanged +=
                OnPositionSettingsPlacementPreferenceChanged;
            _positionSettingsWindow.HorizontalOffsetsChanged +=
                OnPositionSettingsHorizontalOffsetsChanged;
            _positionSettingsWindow.Closed += OnPositionSettingsWindowClosed;
            _positionSettingsWindow.ApplyHorizontalOffsetRanges(
                _taskbarHorizontalOffsetRange,
                _codexHorizontalOffsetRange);
            PositionPlacementSettingsWindow();
            CloseContextMenu();
            _positionSettingsWindow.Show();
        }
        else
        {
            if (_positionSettingsWindow.WindowState == WindowState.Minimized)
            {
                _positionSettingsWindow.WindowState = WindowState.Normal;
            }

            PositionPlacementSettingsWindow();
            CloseContextMenu();
            _positionSettingsWindow.Activate();
        }
    }

    private void CloseContextMenu()
    {
        if (WidgetRoot.ContextMenu is { IsOpen: true } menu)
        {
            menu.IsOpen = false;
        }
    }

    private void OnPositionSettingsPlacementPreferenceChanged(
        object? sender,
        EventArgs eventArgs)
        => PlacementPreferenceChanged?.Invoke(this, EventArgs.Empty);

    private void OnPositionSettingsHorizontalOffsetsChanged(
        object? sender,
        EventArgs eventArgs) =>
        HorizontalOffsetsChanged?.Invoke(this, EventArgs.Empty);

    private void OnPositionSettingsWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (_positionSettingsWindow is not { } window)
        {
            return;
        }

        window.PlacementPreferenceChanged -=
            OnPositionSettingsPlacementPreferenceChanged;
        window.HorizontalOffsetsChanged -= OnPositionSettingsHorizontalOffsetsChanged;
        window.Closed -= OnPositionSettingsWindowClosed;
        _positionSettingsWindow = null;
    }

    private void PositionPlacementSettingsWindow()
    {
        if (_positionSettingsWindow == null ||
            WidgetRoot.ContextMenu is not { } menu)
        {
            return;
        }

        menu.ApplyTemplate();
        if (menu.Template.FindName("ContextMenuPanel", menu) is FrameworkElement panel)
        {
            _positionSettingsWindow.PositionRightOf(panel);
        }
    }

    private void PositionDebugWindow()
    {
        if (_debugWindow == null ||
            WidgetRoot.ContextMenu is not { } menu)
        {
            return;
        }

        menu.ApplyTemplate();
        if (menu.Template.FindName("ContextMenuPanel", menu) is FrameworkElement panel)
        {
            _debugWindow.PositionRightOf(panel);
        }
    }

    private void OnExitMenuClick(object sender, RoutedEventArgs eventArgs) => _exit();

    private sealed class NullRefreshRequester : IRefreshRequester
    {
        public static NullRefreshRequester Instance { get; } = new();

        public Task RequestRefresh(RefreshReason reason) => Task.CompletedTask;
    }

    private sealed class NullStartupRegistration : IStartupRegistration
    {
        public static NullStartupRegistration Instance { get; } = new();

        public bool IsEnabled => false;

        public void SetEnabled(bool enabled)
        {
        }
    }
}
