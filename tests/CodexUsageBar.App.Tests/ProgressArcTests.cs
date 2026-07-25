using System.Windows;
using System.Windows.Media;
using CodexUsageBar.App.Controls;

namespace CodexUsageBar.App.Tests;

public sealed class ProgressArcTests
{
    [Fact]
    public void ZeroProgress_HasEmptyGeometry() => StaTest.Run(() =>
    {
        var arc = ArrangeArc(0);

        Assert.True(arc.ProgressGeometry.IsEmpty());
    });

    [Fact]
    public void FiftyPercent_RoundedCapsVisibleSweepIsHalfCircle() => StaTest.Run(() =>
    {
        var arc = ArrangeArc(50);

        var geometry = Assert.IsType<PathGeometry>(arc.ProgressGeometry);
        var figure = Assert.Single(geometry.Figures);
        var segment = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));
        Assert.Equal(new Size(14.4, 14.4), segment.Size);
        Assert.False(segment.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, segment.SweepDirection);

        Assert.Equal(
            180d,
            VisibleSweepDegrees(arc, figure.StartPoint, segment.Point),
            precision: 8);
    });

    [Theory]
    [InlineData(93d)]
    [InlineData(96d)]
    public void RoundedCaps_VisibleSweepMatchesProgress(double progress) => StaTest.Run(() =>
    {
        var arc = ArrangeArc(progress);
        var geometry = Assert.IsType<PathGeometry>(arc.ProgressGeometry);
        var figure = Assert.Single(geometry.Figures);
        var segment = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        Assert.Equal(
            progress * 3.6d,
            VisibleSweepDegrees(arc, figure.StartPoint, segment.Point),
            precision: 8);
    });

    [Fact]
    public void TinyNonZeroProgress_HasMinimumArcSegment() => StaTest.Run(() =>
    {
        var arc = ArrangeArc(0.0001d);
        var geometry = Assert.IsType<PathGeometry>(arc.ProgressGeometry);
        var figure = Assert.Single(geometry.Figures);
        var segment = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        Assert.Equal(0.01d, CenterlineSweepDegrees(figure.StartPoint, segment.Point), precision: 8);
    });

    [Fact]
    public void FullProgress_UsesTwoHalfCircleArcs() => StaTest.Run(() =>
    {
        var arc = ArrangeArc(100);

        var segments = GetArcSegments(arc);
        Assert.Equal(2, segments.Count);
        Assert.All(segments, segment => Assert.False(segment.IsLargeArc));
        Assert.All(segments, segment => Assert.Equal(SweepDirection.Clockwise, segment.SweepDirection));
    });

    [Theory]
    [InlineData(-12, 0)]
    [InlineData(120, 100)]
    public void Progress_ClampsOutOfRangeValues(double input, double expected) => StaTest.Run(() =>
    {
        var arc = ArrangeArc(input);

        Assert.Equal(expected, arc.Progress);
        _ = arc.ProgressGeometry;
    });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Progress_CoercesNonFiniteValuesToZero(double input) => StaTest.Run(() =>
    {
        var arc = ArrangeArc(input);

        Assert.Equal(0, arc.Progress);
        Assert.True(arc.ProgressGeometry.IsEmpty());
    });

    private static ProgressArc ArrangeArc(double progress)
    {
        var arc = new ProgressArc
        {
            Width = 32,
            Height = 32,
            StrokeThickness = 3.2,
            Progress = progress,
        };

        arc.Measure(new Size(32, 32));
        arc.Arrange(new Rect(0, 0, 32, 32));
        return arc;
    }

    private static IReadOnlyList<ArcSegment> GetArcSegments(ProgressArc arc)
    {
        var geometry = Assert.IsType<PathGeometry>(arc.ProgressGeometry);
        var figure = Assert.Single(geometry.Figures);
        return figure.Segments.Cast<ArcSegment>().ToArray();
    }

    private static double VisibleSweepDegrees(ProgressArc arc, Point startPoint, Point endPoint)
    {
        const double radius = 14.4d;
        var center = new Point(16d, 16d);
        var roundedCapSweep = arc.StrokeThickness / radius * 180d / Math.PI;
        return CenterlineSweepDegrees(startPoint, endPoint) + roundedCapSweep;
    }

    private static double CenterlineSweepDegrees(Point startPoint, Point endPoint)
    {
        var center = new Point(16d, 16d);
        return ClockwiseSweepDegrees(
            AngleDegrees(center, startPoint),
            AngleDegrees(center, endPoint));
    }

    private static double AngleDegrees(Point center, Point point) =>
        Math.Atan2(point.Y - center.Y, point.X - center.X) * 180d / Math.PI;

    private static double ClockwiseSweepDegrees(double startDegrees, double endDegrees) =>
        (endDegrees - startDegrees + 360d) % 360d;
}
