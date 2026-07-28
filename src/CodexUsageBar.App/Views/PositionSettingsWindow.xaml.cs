using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexUsageBar.App.Services;
using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.App.Views;

public partial class PositionSettingsWindow : Window
{
    private const double SliderExtent = 100d;
    private static readonly HorizontalOffsetRange FallbackRange =
        new(-4096d, 4096d);
    private readonly IWidgetPreferences _preferences;
    private bool _isUpdatingControls;
    private HorizontalOffsetRange _taskbarRange = new(0d, 4096d);
    private HorizontalOffsetRange _codexSidebarRange = FallbackRange;
    private double _displayedTaskbarOffsetDip;
    private double _displayedCodexSidebarOffsetDip;

    internal PositionSettingsWindow(
        IWidgetPreferences preferences,
        SystemTheme systemTheme)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        InitializeComponent();
        ApplySystemTheme(systemTheme);
        ApplyPlacementPreference(_preferences.PlacementPreference);
        InitializeControls();
    }

    internal event EventHandler? PlacementPreferenceChanged;

    internal event EventHandler? HorizontalOffsetsChanged;

    internal void ApplySystemTheme(SystemTheme theme) =>
        SystemThemeResources.Replace(Resources, theme);

    internal void ApplyPlacementPreference(WidgetPlacementPreference preference)
    {
        PlacementAutomaticChoice.IsChecked =
            preference == WidgetPlacementPreference.Automatic;
        PlacementTaskbarChoice.IsChecked =
            preference == WidgetPlacementPreference.TaskbarPreferred;
        PlacementCodexSidebarChoice.IsChecked =
            preference == WidgetPlacementPreference.CodexSidebarPreferred;
        PlacementSystemTrayChoice.IsChecked =
            preference == WidgetPlacementPreference.SystemTrayOnly;
    }

    internal void ApplyHorizontalOffsetRanges(
        HorizontalOffsetRange? taskbarRange,
        HorizontalOffsetRange? codexSidebarRange)
    {
        _isUpdatingControls = true;
        try
        {
            if (taskbarRange is { } availableTaskbarRange)
            {
                _taskbarRange = new HorizontalOffsetRange(
                    0d,
                    Math.Max(0d, availableTaskbarRange.MaximumDip));
                ApplyTaskbarHorizontalOffsetRange(
                    TaskbarHorizontalOffsetSlider,
                    _taskbarRange,
                    _preferences.TaskbarHorizontalOffsetDip,
                    out _displayedTaskbarOffsetDip);
            }

            if (codexSidebarRange is { } availableCodexRange)
            {
                _codexSidebarRange = availableCodexRange;
                ApplyHorizontalOffsetRange(
                    CodexHorizontalOffsetSlider,
                    availableCodexRange,
                    _preferences.CodexSidebarHorizontalOffsetDip,
                    out _displayedCodexSidebarOffsetDip);
            }

            UpdateValueText();
        }
        finally
        {
            _isUpdatingControls = false;
        }
    }

    internal void PositionRightOf(FrameworkElement anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.ActualWidth <= 0 ||
            PresentationSource.FromVisual(anchor)?.CompositionTarget is not { } compositionTarget)
        {
            return;
        }

        var anchorTopRightPixels = anchor.PointToScreen(new Point(anchor.ActualWidth, 0));
        var position = DebugWindow.CalculateRightPlacement(
            anchorTopRightPixels,
            compositionTarget.TransformFromDevice,
            new Size(Width, Height),
            new Size(PositionPanel.Width, PositionPanel.Height),
            SystemParameters.WorkArea);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = position.X;
        Top = position.Y;
    }

    private void InitializeControls()
    {
        _isUpdatingControls = true;
        try
        {
            ApplyTaskbarHorizontalOffsetRange(
                TaskbarHorizontalOffsetSlider,
                _taskbarRange,
                _preferences.TaskbarHorizontalOffsetDip,
                out _displayedTaskbarOffsetDip);
            ApplyHorizontalOffsetRange(
                CodexHorizontalOffsetSlider,
                _codexSidebarRange,
                _preferences.CodexSidebarHorizontalOffsetDip,
                out _displayedCodexSidebarOffsetDip);
            UpdateValueText();
        }
        finally
        {
            _isUpdatingControls = false;
        }
    }

    private static void ApplyHorizontalOffsetRange(
        Slider slider,
        HorizontalOffsetRange range,
        double requestedValue,
        out double displayedOffsetDip)
    {
        displayedOffsetDip = range.Clamp(requestedValue);
        slider.Minimum = -SliderExtent;
        slider.Maximum = SliderExtent;
        slider.Value = ToSliderPosition(displayedOffsetDip, range);
    }

    private static void ApplyTaskbarHorizontalOffsetRange(
        Slider slider,
        HorizontalOffsetRange range,
        double requestedValue,
        out double displayedOffsetDip)
    {
        displayedOffsetDip = Math.Clamp(
            double.IsFinite(requestedValue) ? requestedValue : 0d,
            0d,
            Math.Max(0d, range.MaximumDip));
        slider.Minimum = 0d;
        slider.Maximum = SliderExtent;
        slider.Value = range.MaximumDip > 0d
            ? displayedOffsetDip / range.MaximumDip * SliderExtent
            : 0d;
    }

    private void OnTaskbarHorizontalOffsetValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        _displayedTaskbarOffsetDip = Math.Round(
            Math.Max(0d, _taskbarRange.MaximumDip) *
            eventArgs.NewValue / SliderExtent);
        _preferences.TaskbarHorizontalOffsetDip = _displayedTaskbarOffsetDip;
        UpdateValueText();
        HorizontalOffsetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCodexHorizontalOffsetValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        _displayedCodexSidebarOffsetDip = Math.Round(
            FromSliderPosition(eventArgs.NewValue, _codexSidebarRange));
        _preferences.CodexSidebarHorizontalOffsetDip =
            _displayedCodexSidebarOffsetDip;
        UpdateValueText();
        HorizontalOffsetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateValueText()
    {
        TaskbarHorizontalOffsetValueText.Text =
            FormatHorizontalOffset(_displayedTaskbarOffsetDip);
        CodexHorizontalOffsetValueText.Text =
            FormatHorizontalOffset(_displayedCodexSidebarOffsetDip);
    }

    private static double ToSliderPosition(
        double offsetDip,
        HorizontalOffsetRange range)
    {
        if (offsetDip > 0d && range.MaximumDip > 0d)
        {
            return Math.Clamp(
                Math.Sqrt(offsetDip / range.MaximumDip) * SliderExtent,
                0d,
                SliderExtent);
        }

        if (offsetDip < 0d && range.MinimumDip < 0d)
        {
            return Math.Clamp(
                -offsetDip / range.MinimumDip * SliderExtent,
                -SliderExtent,
                0d);
        }

        return 0d;
    }

    private static double FromSliderPosition(
        double sliderPosition,
        HorizontalOffsetRange range)
    {
        if (sliderPosition > 0d)
        {
            var normalizedPosition = sliderPosition / SliderExtent;
            return Math.Max(0d, range.MaximumDip) *
                normalizedPosition *
                normalizedPosition;
        }

        if (sliderPosition < 0d)
        {
            return Math.Min(0d, range.MinimumDip) *
                -sliderPosition / SliderExtent;
        }

        return 0d;
    }

    private static string FormatHorizontalOffset(double value)
    {
        var rounded = Math.Round(value);
        return rounded switch
        {
            > 0d => $"+{rounded:0} px",
            < 0d => $"{rounded:0} px",
            _ => "0 px",
        };
    }

    private void OnPlacementAutomaticClick(object sender, RoutedEventArgs eventArgs) =>
        SetPlacementPreference(WidgetPlacementPreference.Automatic);

    private void OnPlacementTaskbarClick(object sender, RoutedEventArgs eventArgs) =>
        SetPlacementPreference(WidgetPlacementPreference.TaskbarPreferred);

    private void OnPlacementCodexSidebarClick(object sender, RoutedEventArgs eventArgs) =>
        SetPlacementPreference(WidgetPlacementPreference.CodexSidebarPreferred);

    private void OnPlacementSystemTrayClick(object sender, RoutedEventArgs eventArgs) =>
        SetPlacementPreference(WidgetPlacementPreference.SystemTrayOnly);

    private void SetPlacementPreference(WidgetPlacementPreference preference)
    {
        _preferences.PlacementPreference = preference;
        ApplyPlacementPreference(preference);
        PlacementPreferenceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Close();
        }
    }
}
