using System.Windows;
using System.Windows.Media;
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
        var meter = new QuotaMeter { IsRefreshing = true };

        meter.ApplyRefreshVisualState(animationsEnabled: true);

        Assert.True(meter.ArcRotation.HasAnimatedProperties);
        Assert.True(meter.Halo.HasAnimatedProperties);
        Assert.Equal(1d, meter.Arc.Opacity);
    });

    [Fact]
    public void RefreshingWithAnimationsDisabled_ShowsStaticWeakHalo() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter { IsRefreshing = true };

        meter.ApplyRefreshVisualState(animationsEnabled: false);

        Assert.False(meter.ArcRotation.HasAnimatedProperties);
        Assert.False(meter.Halo.HasAnimatedProperties);
        Assert.Equal(0d, meter.ArcRotation.Angle);
        Assert.Equal(0.24d, meter.Halo.Opacity);
    });

    [Fact]
    public void LeavingRefreshState_RemovesClocksAndRestoresRestingValues() => StaTest.Run(() =>
    {
        var meter = new QuotaMeter { IsRefreshing = true };
        meter.ApplyRefreshVisualState(animationsEnabled: true);

        meter.IsRefreshing = false;
        meter.ApplyRefreshVisualState(animationsEnabled: true);

        Assert.False(meter.ArcRotation.HasAnimatedProperties);
        Assert.False(meter.Halo.HasAnimatedProperties);
        Assert.Equal(0d, meter.ArcRotation.Angle);
        Assert.Equal(0d, meter.Halo.Opacity);
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
}
