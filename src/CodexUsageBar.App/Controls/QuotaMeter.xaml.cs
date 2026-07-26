using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexUsageBar.App.Controls;

public partial class QuotaMeter : UserControl
{
    private static readonly Duration RotationDuration = new(TimeSpan.FromSeconds(1.2));
    private static readonly Duration BreathingDuration = new(TimeSpan.FromSeconds(0.8));
    private static readonly Duration ExitFadeDuration = new(TimeSpan.FromMilliseconds(180));
    private const double ActiveRefreshHighlightOpacity = 1d;
    private readonly DispatcherTimer _exitFadeTimer;
    private bool _animationsEnabled = SystemParameters.ClientAreaAnimation;

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

    public static readonly DependencyProperty RefreshAnimationStyleProperty = DependencyProperty.Register(
        nameof(RefreshAnimationStyle),
        typeof(RefreshAnimationStyle),
        typeof(QuotaMeter),
        new FrameworkPropertyMetadata(
            RefreshAnimationStyle.ProgressRing,
            OnRefreshAnimationStyleChanged));

    public QuotaMeter()
    {
        InitializeComponent();
        _exitFadeTimer = new DispatcherTimer(
            DispatcherPriority.Render,
            Dispatcher)
        {
            Interval = ExitFadeDuration.TimeSpan,
        };
        _exitFadeTimer.Tick += OnExitFadeElapsed;
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

    public RefreshAnimationStyle RefreshAnimationStyle
    {
        get => (RefreshAnimationStyle)GetValue(RefreshAnimationStyleProperty);
        set => SetValue(RefreshAnimationStyleProperty, value);
    }

    private static object CoerceRingDiameter(DependencyObject _, object value) =>
        Math.Max(22d, (double)value);

    private static void OnRingDiameterChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((QuotaMeter)dependencyObject).ApplyDiameter((double)e.NewValue);

    private static void OnIsRefreshingChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs) =>
        ((QuotaMeter)dependencyObject).ApplyRefreshVisualState();

    private static void OnRefreshAnimationStyleChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs) =>
        ((QuotaMeter)dependencyObject).ApplyRefreshVisualState();

    private void ApplyDiameter(double diameter)
    {
        var strokeThickness = diameter * 0.1d;
        Track.StrokeThickness = strokeThickness;
        Arc.StrokeThickness = strokeThickness;
        RefreshArc.StrokeThickness = strokeThickness;
        Halo.StrokeThickness = strokeThickness * 1.45d;
        var dotDiameter = strokeThickness * 0.9d;
        RefreshDot.Width = dotDiameter;
        RefreshDot.Height = dotDiameter;
        RefreshDot.Margin = new Thickness(0d, (strokeThickness - dotDiameter) / 2d, 0d, 0d);
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyRefreshVisualState(SystemParameters.ClientAreaAnimation);
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        ResetRefreshVisuals();
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
        _animationsEnabled = animationsEnabled;
        ApplyRefreshVisualState();
    }

    private void ApplyRefreshVisualState()
    {
        if (IsRefreshing)
        {
            StartRefreshVisuals(_animationsEnabled);
            return;
        }

        StopRefreshVisuals(_animationsEnabled);
    }

    private void StartRefreshVisuals(bool animationsEnabled)
    {
        var currentAngle = NormalizeAngle(
            RefreshArcRotation.HasAnimatedProperties
                ? RefreshArcRotation.Angle
                : RefreshDotRotation.Angle);
        _exitFadeTimer.Stop();
        ClearRefreshAnimationClocks();
        RefreshArcRotation.Angle = currentAngle;
        RefreshDotRotation.Angle = currentAngle;
        RefreshArc.Opacity = 0d;
        RefreshDotOrbit.Opacity = 0d;
        Arc.Visibility = Visibility.Visible;

        if (!animationsEnabled)
        {
            RefreshArcRotation.Angle = 0d;
            RefreshDotRotation.Angle = 0d;
            Halo.Opacity = 0.24d;
            return;
        }

        switch (RefreshAnimationStyle)
        {
            case RefreshAnimationStyle.ProgressRing:
                RefreshArc.SetResourceReference(
                    System.Windows.Shapes.Shape.StrokeProperty,
                    "QuotaProgressBrush");
                RefreshArc.Progress = Arc.Progress;
                RefreshArc.Opacity = Arc.Opacity;
                Arc.Visibility = Visibility.Hidden;
                StartRotation(RefreshArcRotation, currentAngle);
                break;
            case RefreshAnimationStyle.HighlightSweep:
                RefreshArc.SetResourceReference(
                    System.Windows.Shapes.Shape.StrokeProperty,
                    "QuotaRefreshHighlightBrush");
                RefreshArc.Progress = 20d;
                RefreshArc.Opacity = ActiveRefreshHighlightOpacity;
                StartRotation(RefreshArcRotation, currentAngle);
                break;
            case RefreshAnimationStyle.DotOrbit:
                RefreshDotOrbit.Opacity = ActiveRefreshHighlightOpacity;
                StartRotation(RefreshDotRotation, currentAngle);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported refresh animation style: {RefreshAnimationStyle}.");
        }

        Halo.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.16d, 0.42d, BreathingDuration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }

    private void StopRefreshVisuals(bool animationsEnabled)
    {
        if (!animationsEnabled
            || (RefreshArc.Opacity <= 0d
                && RefreshDotOrbit.Opacity <= 0d
                && !RefreshArc.HasAnimatedProperties
                && !RefreshArcRotation.HasAnimatedProperties
                && !RefreshDotOrbit.HasAnimatedProperties
                && !RefreshDotRotation.HasAnimatedProperties
                && !Halo.HasAnimatedProperties))
        {
            ResetRefreshVisuals();
            return;
        }

        var refreshArcOpacity = RefreshArc.Opacity;
        var refreshDotOpacity = RefreshDotOrbit.Opacity;
        var haloOpacity = Halo.Opacity;
        var staticArcOpacity = Arc.Opacity;
        var shouldCrossfadeStaticArc = Arc.Visibility != Visibility.Visible;
        Arc.Visibility = Visibility.Visible;
        if (shouldCrossfadeStaticArc)
        {
            Arc.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0d, staticArcOpacity, ExitFadeDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
        }

        var exitEasing = new CubicEase { EasingMode = EasingMode.EaseIn };
        RefreshArc.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(refreshArcOpacity, 0d, ExitFadeDuration)
            {
                EasingFunction = exitEasing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        RefreshDotOrbit.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(refreshDotOpacity, 0d, ExitFadeDuration)
            {
                EasingFunction = exitEasing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        Halo.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(haloOpacity, 0d, ExitFadeDuration)
            {
                EasingFunction = exitEasing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        _exitFadeTimer.Stop();
        _exitFadeTimer.Start();
    }

    private void OnExitFadeElapsed(object? sender, EventArgs eventArgs)
    {
        _exitFadeTimer.Stop();
        if (!IsRefreshing)
        {
            ResetRefreshVisuals();
        }
    }

    private void ResetRefreshVisuals()
    {
        _exitFadeTimer.Stop();
        ClearRefreshAnimationClocks();
        Arc.Visibility = Visibility.Visible;
        RefreshArcRotation.Angle = 0d;
        RefreshDotRotation.Angle = 0d;
        RefreshArc.Opacity = 0d;
        RefreshDotOrbit.Opacity = 0d;
        Halo.Opacity = 0d;
    }

    private void ClearRefreshAnimationClocks()
    {
        RefreshArcRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            null);
        RefreshDotRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            null);
        Arc.BeginAnimation(OpacityProperty, null);
        RefreshArc.BeginAnimation(OpacityProperty, null);
        RefreshDotOrbit.BeginAnimation(OpacityProperty, null);
        Halo.BeginAnimation(OpacityProperty, null);
    }

    private static void StartRotation(
        System.Windows.Media.RotateTransform rotation,
        double currentAngle) =>
        rotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(currentAngle, currentAngle + 360d, RotationDuration)
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });

    private static double NormalizeAngle(double angle) =>
        ((angle % 360d) + 360d) % 360d;
}
