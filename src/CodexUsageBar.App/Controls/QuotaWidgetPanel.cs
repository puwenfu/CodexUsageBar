using System.Windows;
using System.Windows.Controls;

namespace CodexUsageBar.App.Controls;

public sealed class QuotaWidgetPanel : Panel
{
    private const double MeterToTimeGapDip = 2d;
    private const double GroupGapDip = 4d;
    private const double FiveHourShare = 0.4d;
    private const double TrailingSafetyInsetDip = 1d;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count != 4)
        {
            return MeasureFallback(availableSize);
        }

        if (IsFiveHourGroupCollapsed())
        {
            return MeasureWeeklyOnly(availableSize);
        }

        var constrainedHeight = double.IsFinite(availableSize.Height) ? availableSize.Height : double.PositiveInfinity;
        InternalChildren[0].Measure(new Size(double.PositiveInfinity, constrainedHeight));
        InternalChildren[2].Measure(new Size(double.PositiveInfinity, constrainedHeight));
        var firstMeterWidth = InternalChildren[0].DesiredSize.Width;
        var secondMeterWidth = InternalChildren[2].DesiredSize.Width;
        var targetWidth = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : firstMeterWidth + secondMeterWidth + TotalGapDip + 96d;
        var (fiveHourWidth, weeklyWidth) = AllocateResetWidths(targetWidth, firstMeterWidth, secondMeterWidth);

        ConstrainWidth(InternalChildren[1], fiveHourWidth);
        ConstrainWidth(InternalChildren[3], weeklyWidth);
        InternalChildren[1].Measure(new Size(fiveHourWidth, constrainedHeight));
        InternalChildren[3].Measure(new Size(weeklyWidth, constrainedHeight));

        var desiredHeight = InternalChildren.Cast<UIElement>().Max(child => child.DesiredSize.Height);
        return new Size(targetWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count != 4)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(finalSize));
            }

            return finalSize;
        }

        if (IsFiveHourGroupCollapsed())
        {
            return ArrangeWeeklyOnly(finalSize);
        }

        var firstMeterWidth = InternalChildren[0].DesiredSize.Width;
        var secondMeterWidth = InternalChildren[2].DesiredSize.Width;
        var (fiveHourWidth, weeklyWidth) = AllocateResetWidths(finalSize.Width, firstMeterWidth, secondMeterWidth);
        ConstrainWidth(InternalChildren[1], fiveHourWidth);
        ConstrainWidth(InternalChildren[3], weeklyWidth);
        var x = 0d;

        ArrangeVerticallyCentered(InternalChildren[0], x, firstMeterWidth, finalSize.Height);
        x += firstMeterWidth + MeterToTimeGapDip;
        ArrangeVerticallyCentered(InternalChildren[1], x, fiveHourWidth, finalSize.Height);
        x += fiveHourWidth + GroupGapDip;
        ArrangeVerticallyCentered(InternalChildren[2], x, secondMeterWidth, finalSize.Height);
        x += secondMeterWidth + MeterToTimeGapDip;
        ArrangeVerticallyCentered(InternalChildren[3], x, weeklyWidth, finalSize.Height);
        return finalSize;
    }

    private static double TotalGapDip => (MeterToTimeGapDip * 2d) + GroupGapDip;

    private bool IsFiveHourGroupCollapsed() =>
        InternalChildren[0].Visibility == Visibility.Collapsed &&
        InternalChildren[1].Visibility == Visibility.Collapsed;

    private Size MeasureWeeklyOnly(Size availableSize)
    {
        var constrainedHeight = double.IsFinite(availableSize.Height)
            ? availableSize.Height
            : double.PositiveInfinity;
        InternalChildren[0].Measure(new Size(0, 0));
        InternalChildren[1].Measure(new Size(0, 0));
        InternalChildren[2].Measure(new Size(double.PositiveInfinity, constrainedHeight));
        var meterWidth = InternalChildren[2].DesiredSize.Width;
        var targetWidth = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : meterWidth + MeterToTimeGapDip + 64d;
        var resetWidth = Math.Max(
            0d,
            targetWidth - meterWidth - MeterToTimeGapDip - TrailingSafetyInsetDip);
        ConstrainWidth(InternalChildren[3], resetWidth);
        InternalChildren[3].Measure(new Size(resetWidth, constrainedHeight));
        var desiredHeight = Math.Max(
            InternalChildren[2].DesiredSize.Height,
            InternalChildren[3].DesiredSize.Height);
        return new Size(targetWidth, desiredHeight);
    }

    private Size ArrangeWeeklyOnly(Size finalSize)
    {
        InternalChildren[0].Arrange(Rect.Empty);
        InternalChildren[1].Arrange(Rect.Empty);
        var meterWidth = InternalChildren[2].DesiredSize.Width;
        var resetWidth = Math.Max(
            0d,
            finalSize.Width - meterWidth - MeterToTimeGapDip - TrailingSafetyInsetDip);
        ConstrainWidth(InternalChildren[3], resetWidth);
        ArrangeVerticallyCentered(InternalChildren[2], 0d, meterWidth, finalSize.Height);
        ArrangeVerticallyCentered(
            InternalChildren[3],
            meterWidth + MeterToTimeGapDip,
            resetWidth,
            finalSize.Height);
        return finalSize;
    }

    private static (double FiveHour, double Weekly) AllocateResetWidths(
        double totalWidth,
        double firstMeterWidth,
        double secondMeterWidth)
    {
        var remaining = Math.Max(
            0d,
            totalWidth - firstMeterWidth - secondMeterWidth - TotalGapDip - TrailingSafetyInsetDip);
        var fiveHour = remaining * FiveHourShare;
        return (fiveHour, remaining - fiveHour);
    }

    private static void ConstrainWidth(UIElement child, double width)
    {
        if (child is FrameworkElement element)
        {
            element.Width = width;
            element.MaxWidth = width;
        }
    }

    private static void ArrangeVerticallyCentered(UIElement child, double x, double width, double height)
    {
        var childHeight = Math.Min(height, child.DesiredSize.Height);
        var y = Math.Max(0d, (height - childHeight) / 2d);
        child.Arrange(new Rect(x, y, width, childHeight));
    }

    private Size MeasureFallback(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
        }

        var width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : InternalChildren.Cast<UIElement>().Sum(child => child.DesiredSize.Width);
        var height = double.IsFinite(availableSize.Height)
            ? availableSize.Height
            : InternalChildren.Cast<UIElement>().DefaultIfEmpty().Max(child => child?.DesiredSize.Height ?? 0d);
        return new Size(width, height);
    }
}
