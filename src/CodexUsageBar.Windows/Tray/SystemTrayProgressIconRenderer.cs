using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageBar.Windows.Interop;
using MediaGeometry = System.Windows.Media.Geometry;

namespace CodexUsageBar.Windows.Tray;

internal static class SystemTrayProgressIconRenderer
{
    private const int IconSize = 32;
    private const double StrokeRatio = 0.15625d;
    private const double OuterMarginRatio = 0d;
    private const double MinimumSweepDegrees = 0.01d;

    public static nint CreateIcon(SystemTrayIconState state)
    {
        var bitmap = Render(state);
        var stride = checked(IconSize * 4);
        var pixels = new byte[checked(stride * IconSize)];
        bitmap.CopyPixels(pixels, stride, 0);

        var colorBitmap = NativeMethods.CreateBitmap(
            IconSize,
            IconSize,
            1,
            32,
            pixels);
        if (colorBitmap == 0)
        {
            return 0;
        }

        var maskBitmap = NativeMethods.CreateBitmap(
            IconSize,
            IconSize,
            1,
            1,
            bits: null);
        if (maskBitmap == 0)
        {
            _ = NativeMethods.DeleteObject(colorBitmap);
            return 0;
        }

        try
        {
            var iconInfo = new NativeMethods.ICONINFO
            {
                IsIcon = true,
                ColorBitmap = colorBitmap,
                MaskBitmap = maskBitmap,
            };
            return NativeMethods.CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(maskBitmap);
            _ = NativeMethods.DeleteObject(colorBitmap);
        }
    }

    internal static double CalculateCenterlineSweepDegrees(
        double progress,
        double stroke,
        double radius)
    {
        var targetVisibleSweep = Math.Clamp(progress, 0d, 100d) * 3.6d;
        var roundedCapsSweep = stroke / radius * 180d / Math.PI;
        return Math.Max(MinimumSweepDegrees, targetVisibleSweep - roundedCapsSweep);
    }

    private static RenderTargetBitmap Render(SystemTrayIconState state)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var stroke = IconSize * StrokeRatio;
            var outerMargin = IconSize * OuterMarginRatio;
            var radius = (IconSize - (2d * outerMargin) - stroke) / 2d;
            var center = new Point(IconSize / 2d, IconSize / 2d);
            drawing.DrawEllipse(
                brush: null,
                new Pen(new SolidColorBrush(state.TrackColor), stroke),
                center,
                radius,
                radius);

            var progress = Math.Clamp(state.Progress, 0d, 100d);
            if (progress > 0d)
            {
                var progressPen = new Pen(CreateGradient(state), stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                if (progress >= 100d)
                {
                    drawing.DrawEllipse(
                        brush: null,
                        progressPen,
                        center,
                        radius,
                        radius);
                }
                else
                {
                    drawing.DrawGeometry(
                        brush: null,
                        progressPen,
                        CreateArcGeometry(
                            center,
                            radius,
                            CalculateCenterlineSweepDegrees(
                                progress,
                                stroke,
                                radius)));
                }
            }

            DrawCenteredText(drawing, state);
        }

        var bitmap = new RenderTargetBitmap(
            IconSize,
            IconSize,
            96d,
            96d,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawCenteredText(
        DrawingContext drawing,
        SystemTrayIconState state)
    {
        var text = string.IsNullOrWhiteSpace(state.Text)
            ? "--"
            : state.Text.Trim();
        var isThreeDigits = text.Length == 3;
        var isFullLabel = string.Equals(text, "满", StringComparison.Ordinal);
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(
                    isThreeDigits
                        ? "Bahnschrift SemiCondensed"
                        : isFullLabel
                            ? "Microsoft YaHei UI"
                        : "Segoe UI"),
                FontStyles.Normal,
                FontWeights.Bold,
                isThreeDigits
                    ? FontStretches.SemiCondensed
                    : FontStretches.Normal),
            isThreeDigits ? 10d : isFullLabel ? 12d : 12.5d,
            new SolidColorBrush(state.TextColor),
            pixelsPerDip: 1d)
        {
            TextAlignment = TextAlignment.Center,
        };
        drawing.DrawText(
            formattedText,
            new Point(
                IconSize / 2d,
                (IconSize - formattedText.Height) / 2d));
    }

    private static LinearGradientBrush CreateGradient(SystemTrayIconState state)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(state.GradientStartColor, 0d));
        brush.GradientStops.Add(new GradientStop(state.GradientMiddleColor, 0.52d));
        brush.GradientStops.Add(new GradientStop(state.GradientEndColor, 1d));
        brush.Freeze();
        return brush;
    }

    private static MediaGeometry CreateArcGeometry(
        Point center,
        double radius,
        double sweepDegrees)
    {
        var start = PointOnCircle(center, radius, -90d);
        var end = PointOnCircle(center, radius, -90d + sweepDegrees);
        var geometry = new PathGeometry(
        [
            new PathFigure(
                start,
                [
                    new ArcSegment(
                        end,
                        new Size(radius, radius),
                        rotationAngle: 0d,
                        isLargeArc: sweepDegrees > 180d,
                        SweepDirection.Clockwise,
                        isStroked: true),
                ],
                closed: false),
        ]);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(
        Point center,
        double radius,
        double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
