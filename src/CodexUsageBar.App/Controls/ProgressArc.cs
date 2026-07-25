using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexUsageBar.App.Controls;

public sealed class ProgressArc : Shape
{
    private const double MinimumNonZeroSweepDegrees = 0.01d;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(ProgressArc),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            null,
            CoerceProgress));

    public ProgressArc()
    {
        StrokeStartLineCap = PenLineCap.Round;
        StrokeEndLineCap = PenLineCap.Round;
        Stretch = Stretch.None;
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    internal Geometry ProgressGeometry => DefiningGeometry;

    protected override Geometry DefiningGeometry
    {
        get
        {
            var progress = Progress;
            var radius = (Math.Min(RenderSize.Width, RenderSize.Height) - StrokeThickness) / 2d;
            if (progress <= 0 || radius <= 0)
            {
                return Geometry.Empty;
            }

            var center = new Point(RenderSize.Width / 2d, RenderSize.Height / 2d);
            var start = PointAt(center, radius, -90);
            var figure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
                IsFilled = false,
            };

            if (progress >= 100)
            {
                figure.Segments.Add(CreateArc(PointAt(center, radius, 90), radius, isLargeArc: false));
                figure.Segments.Add(CreateArc(start, radius, isLargeArc: false));
            }
            else
            {
                var sweep = CalculateCenterlineSweepDegrees(progress, radius, StrokeThickness);
                figure.Segments.Add(CreateArc(PointAt(center, radius, -90 + sweep), radius, sweep > 180));
            }

            return new PathGeometry([figure]);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == StrokeThicknessProperty)
        {
            InvalidateVisual();
        }
    }

    private static object CoerceProgress(DependencyObject _, object value)
    {
        var progress = (double)value;
        return double.IsFinite(progress) ? Math.Clamp(progress, 0d, 100d) : 0d;
    }

    private static ArcSegment CreateArc(Point point, double radius, bool isLargeArc) => new()
    {
        Point = point,
        Size = new Size(radius, radius),
        IsLargeArc = isLargeArc,
        SweepDirection = SweepDirection.Clockwise,
        RotationAngle = 0,
    };

    private static double CalculateCenterlineSweepDegrees(
        double progress,
        double radius,
        double strokeThickness)
    {
        var circumference = 2d * Math.PI * radius;
        var targetVisibleLength = circumference * progress / 100d;
        var centerlineLength = Math.Max(0d, targetVisibleLength - strokeThickness);
        var sweep = centerlineLength / circumference * 360d;
        return Math.Max(MinimumNonZeroSweepDegrees, sweep);
    }

    private static Point PointAt(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }
}
