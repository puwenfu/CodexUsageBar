using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Windows.Startup;

namespace CodexUsageBar.App;

public partial class WidgetWindow : Window
{
    private readonly IRefreshRequester _refreshRequester;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IWidgetPreferences _preferences;
    private readonly Action _exit;
    private readonly DebugViewModel _debugViewModel;
    private Views.DebugWindow? _debugWindow;

    public WidgetWindow(WidgetViewModel viewModel, double taskbarHeightDip, DebugViewModel debugViewModel)
        : this(
            viewModel,
            taskbarHeightDip,
            NullRefreshRequester.Instance,
            NullStartupRegistration.Instance,
            new SessionWidgetPreferences(),
            () => { },
            debugViewModel)
    {
    }

    internal WidgetWindow(
        WidgetViewModel viewModel,
        double taskbarHeightDip,
        IRefreshRequester refreshRequester,
        IStartupRegistration startupRegistration,
        IWidgetPreferences preferences,
        Action exit,
        DebugViewModel debugViewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _refreshRequester = refreshRequester ?? throw new ArgumentNullException(nameof(refreshRequester));
        _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _debugViewModel = debugViewModel ?? throw new ArgumentNullException(nameof(debugViewModel));
        if (!double.IsFinite(taskbarHeightDip) || taskbarHeightDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskbarHeightDip));
        }

        InitializeComponent();
        AboutVersionText.Text = AboutVersionProvider.GetDisplayText(typeof(WidgetWindow).Assembly);
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

    private string _currentTheme = "QuotaTheme.xaml";

    private void OnContextMenuOpened(object sender, RoutedEventArgs eventArgs)
    {
        StartupMenuItem.IsChecked = _startupRegistration.IsEnabled;
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

        if (sender is ContextMenu menu)
        {
            menu.VerticalOffset = 0d;
            _ = menu.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                () => KeepContextMenuOnePhysicalPixelAboveTaskbar(menu));
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

    private void OnThemeBlueClick(object sender, RoutedEventArgs eventArgs) => SetTheme("QuotaTheme.xaml");

    private void OnThemePurpleClick(object sender, RoutedEventArgs eventArgs) => SetTheme("QuotaThemePurple.xaml");

    private void OnThemeRoseClick(object sender, RoutedEventArgs eventArgs) => SetTheme("QuotaThemeRose.xaml");

    private void OnThemeMintClick(object sender, RoutedEventArgs eventArgs) => SetTheme("QuotaThemeMint.xaml");

    private void OnThemeForestClick(object sender, RoutedEventArgs eventArgs) => SetTheme("QuotaThemeForest.xaml");

    private void OnRefreshProgressRingClick(object sender, RoutedEventArgs eventArgs) =>
        SetRefreshAnimationStyle(RefreshAnimationStyle.ProgressRing);

    private void OnRefreshHighlightSweepClick(object sender, RoutedEventArgs eventArgs) =>
        SetRefreshAnimationStyle(RefreshAnimationStyle.HighlightSweep);

    private void OnRefreshDotOrbitClick(object sender, RoutedEventArgs eventArgs) =>
        SetRefreshAnimationStyle(RefreshAnimationStyle.DotOrbit);

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

    private void SetTheme(string themeName)
    {
        if (_currentTheme == themeName &&
            Application.Current.Resources.MergedDictionaries.Any(
                dictionary => dictionary.Source?.ToString().Contains(themeName) == true))
        {
            return;
        }

        _currentTheme = themeName;
        ReplaceTheme(Application.Current.Resources, themeName);
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
        var enabled = !_startupRegistration.IsEnabled;
        _startupRegistration.SetEnabled(enabled);
        StartupMenuItem.IsChecked = enabled;
    }

    private void OnDebugMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_debugWindow == null)
        {
            _debugWindow = new Views.DebugWindow(_debugViewModel);
            _debugWindow.Closed += (s, e) => _debugWindow = null;
            PositionDebugWindow();
            _debugWindow.Show();
        }
        else
        {
            if (_debugWindow.WindowState == WindowState.Minimized)
                _debugWindow.WindowState = WindowState.Normal;
            PositionDebugWindow();
            _debugWindow.Activate();
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
