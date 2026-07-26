using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageBar.App.Controls;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.Core.Presentation;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class QuotaMeterRefreshAnimationTests
{
    [Fact]
    public void RefreshingWithAnimationsEnabled_StartsRotationAndBreathing() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter
        {
            RefreshAnimationStyle = RefreshAnimationStyle.HighlightSweep,
            IsRefreshing = true,
        };

        meter.ApplyRefreshVisualState(animationsEnabled: true);

        Assert.True(meter.RefreshArcRotation.HasAnimatedProperties);
        Assert.True(meter.Halo.HasAnimatedProperties);
        Assert.Equal(1d, meter.RefreshArc.Opacity);
        Assert.Equal(20d, meter.RefreshArc.Progress);
        Assert.Same(Transform.Identity, meter.Arc.RenderTransform);
        Assert.Equal(1d, meter.Arc.Opacity);
    });

    [Fact]
    public void ProgressRingStyle_RotatesQuotaArcCopyWhileStaticArcIsHidden() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter
        {
            RefreshAnimationStyle = RefreshAnimationStyle.ProgressRing,
        };
        meter.Arc.Progress = 72d;
        meter.IsRefreshing = true;

        meter.ApplyRefreshVisualState(animationsEnabled: true);

        Assert.True(meter.RefreshArcRotation.HasAnimatedProperties);
        Assert.Equal(72d, meter.RefreshArc.Progress);
        Assert.Equal(Visibility.Hidden, meter.Arc.Visibility);
    });

    [Fact]
    public void ProgressRingStyle_ExitRestoresStaticArcBeforeOverlayFades() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter
        {
            RefreshAnimationStyle = RefreshAnimationStyle.ProgressRing,
        };
        meter.Arc.Progress = 72d;
        meter.IsRefreshing = true;
        meter.ApplyRefreshVisualState(animationsEnabled: true);

        meter.IsRefreshing = false;

        Assert.Equal(Visibility.Visible, meter.Arc.Visibility);
        Assert.True(meter.RefreshArc.HasAnimatedProperties);
        Assert.True(meter.RefreshArcRotation.HasAnimatedProperties);
    });

    [Fact]
    public void DotOrbitStyle_RotatesSingleDotWithoutHidingQuotaArc() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter
        {
            RefreshAnimationStyle = RefreshAnimationStyle.DotOrbit,
            IsRefreshing = true,
        };

        meter.ApplyRefreshVisualState(animationsEnabled: true);

        Assert.True(meter.RefreshDotRotation.HasAnimatedProperties);
        Assert.Equal(1d, meter.RefreshDotOrbit.Opacity);
        Assert.Equal(Visibility.Visible, meter.Arc.Visibility);
        Assert.Equal(0d, meter.RefreshArc.Opacity);
    });

    [Fact]
    public void RefreshingWithAnimationsDisabled_ShowsStaticWeakHalo() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter { IsRefreshing = true };

        meter.ApplyRefreshVisualState(animationsEnabled: false);

        Assert.False(meter.RefreshArcRotation.HasAnimatedProperties);
        Assert.False(meter.Halo.HasAnimatedProperties);
        Assert.Equal(0d, meter.RefreshArcRotation.Angle);
        Assert.Equal(0d, meter.RefreshArc.Opacity);
        Assert.Equal(0.24d, meter.Halo.Opacity);
    });

    [Fact]
    public void LeavingRefreshState_FadesOverlayWithoutResettingVisibleRotation() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter
        {
            RefreshAnimationStyle = RefreshAnimationStyle.HighlightSweep,
            IsRefreshing = true,
        };
        meter.ApplyRefreshVisualState(animationsEnabled: true);

        meter.IsRefreshing = false;

        Assert.True(meter.RefreshArcRotation.HasAnimatedProperties);
        Assert.True(meter.RefreshArc.HasAnimatedProperties);
        Assert.True(meter.Halo.HasAnimatedProperties);
    });

    [Fact]
    public void LeavingRefreshState_ResetsOverlayOnlyAfterItIsInvisible() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter { IsRefreshing = true };
        var window = new Window
        {
            Content = meter,
            ShowActivated = false,
            ShowInTaskbar = false,
            Width = 64,
            Height = 64,
            WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            meter.ApplyRefreshVisualState(animationsEnabled: true);
            meter.IsRefreshing = false;
            PumpDispatcherFor(TimeSpan.FromMilliseconds(240));

            Assert.False(meter.RefreshArcRotation.HasAnimatedProperties);
            Assert.False(meter.RefreshArc.HasAnimatedProperties);
            Assert.False(meter.RefreshDotRotation.HasAnimatedProperties);
            Assert.False(meter.RefreshDotOrbit.HasAnimatedProperties);
            Assert.False(meter.Halo.HasAnimatedProperties);
            Assert.Equal(0d, meter.RefreshArcRotation.Angle);
            Assert.Equal(0d, meter.RefreshDotRotation.Angle);
            Assert.Equal(0d, meter.RefreshArc.Opacity);
            Assert.Equal(0d, meter.RefreshDotOrbit.Opacity);
            Assert.Equal(0d, meter.Halo.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void RefreshRestartDuringFade_CancelsExitAndContinuesFromCurrentAngle() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter
        {
            RefreshAnimationStyle = RefreshAnimationStyle.HighlightSweep,
            IsRefreshing = true,
        };
        meter.ApplyRefreshVisualState(animationsEnabled: true);
        PumpDispatcherFor(TimeSpan.FromMilliseconds(60));

        meter.IsRefreshing = false;
        var angleAtExit = meter.RefreshArcRotation.Angle;
        meter.IsRefreshing = true;

        Assert.True(meter.RefreshArcRotation.HasAnimatedProperties);
        Assert.False(meter.RefreshArc.HasAnimatedProperties);
        Assert.Equal(1d, meter.RefreshArc.Opacity);
        Assert.True(meter.RefreshArcRotation.Angle >= angleAtExit);
    });

    [Fact]
    public void WindowRefreshState_ReachesBothMeters() => StaTest.Run(() =>
    {
        var display = new WidgetDisplayModel(
            new QuotaDisplayWindow("98%", "5h", "57m", 1),
            new QuotaDisplayWindow("98%", "周", "6d 20h 57m", 1),
            "quota details",
            IsRefreshing: true,
            IsStale: false);
        var window = new WidgetWindow(
            new WidgetViewModel(display, 36),
            48,
            new DebugViewModel());

        try
        {
            window.ShowActivated = false;
            window.Show();
            window.UpdateLayout();

            var meters = FindVisualChildren<QuotaMeter>(window).ToArray();
            Assert.Equal(2, meters.Length);
            Assert.All(meters, meter => Assert.True(meter.IsRefreshing));
        }
        finally
        {
            window.Close();
        }
    });

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher.CurrentDispatcher)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
