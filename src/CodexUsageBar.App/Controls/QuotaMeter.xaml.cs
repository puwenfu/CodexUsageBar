using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CodexUsageBar.App.Controls;

public partial class QuotaMeter : UserControl
{
    private static readonly Duration RotationDuration = new(TimeSpan.FromSeconds(1.2));
    private static readonly Duration BreathingDuration = new(TimeSpan.FromSeconds(0.8));

    public static readonly DependencyProperty RingDiameterProperty = DependencyProperty.Register(
        nameof(RingDiameter),
        typeof(double),
        typeof(QuotaMeter),
        new FrameworkPropertyMetadata(32d, OnRingDiameterChanged, CoerceRingDiameter));

    public static readonly DependencyProperty IsRefreshingProperty = DependencyProperty.Register(
        nameof(IsRefreshing),
        typeof(bool),
        typeof(QuotaMeter),
        new FrameworkPropertyMetadata(false, OnIsRefreshingChanged));

    public QuotaMeter()
    {
        InitializeComponent();
        ApplyDiameter(RingDiameter);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public double RingDiameter
    {
        get => (double)GetValue(RingDiameterProperty);
        set => SetValue(RingDiameterProperty, value);
    }

    public bool IsRefreshing
    {
        get => (bool)GetValue(IsRefreshingProperty);
        set => SetValue(IsRefreshingProperty, value);
    }

    private static object CoerceRingDiameter(DependencyObject _, object value) =>
        Math.Max(22d, (double)value);

    private static void OnRingDiameterChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((QuotaMeter)dependencyObject).ApplyDiameter((double)e.NewValue);

    private static void OnIsRefreshingChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs) =>
        ((QuotaMeter)dependencyObject).ApplyRefreshVisualState(SystemParameters.ClientAreaAnimation);

    private void ApplyDiameter(double diameter)
    {
        var strokeThickness = diameter * 0.1d;
        Track.StrokeThickness = strokeThickness;
        Arc.StrokeThickness = strokeThickness;
        Halo.StrokeThickness = strokeThickness * 1.45d;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyRefreshVisualState(SystemParameters.ClientAreaAnimation);
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        ClearRefreshAnimations();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            ApplyRefreshVisualState(SystemParameters.ClientAreaAnimation);
        }
    }

    internal void ApplyRefreshVisualState(bool animationsEnabled)
    {
        ClearRefreshAnimations();
        if (!IsRefreshing)
        {
            return;
        }

        if (!animationsEnabled)
        {
            Halo.Opacity = 0.24d;
            return;
        }

        ArcRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(0d, 360d, RotationDuration)
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
        Halo.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.16d, 0.42d, BreathingDuration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }

    private void ClearRefreshAnimations()
    {
        ArcRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            null);
        Halo.BeginAnimation(OpacityProperty, null);
        ArcRotation.Angle = 0d;
        Halo.Opacity = 0d;
    }
}
