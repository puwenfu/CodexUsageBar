using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarPlacementCalculatorTests
{
    [Theory]
    [InlineData(96u, 48, 48d)]
    [InlineData(144u, 72, 48d)]
    [InlineData(192u, 96, 48d)]
    public void Calculate_KeepsEquivalentDipHeight(uint dpi, int physicalHeight, double expectedDipHeight)
    {
        var rect = new PhysicalRect(0, 1080 - physicalHeight, 1920, 1080);

        var placement = TaskbarPlacementCalculator.Calculate(rect, dpi);

        Assert.Equal(expectedDipHeight, placement.HeightDip, 3);
        Assert.True(placement.TopDip >= rect.Top / (dpi / 96d));
        Assert.True(placement.BottomPhysicalPixel <= rect.Bottom);
    }

    [Fact]
    public void Calculate_UsesSixPhysicalPixelInsetAtTwoHundredPercent()
    {
        var placement = TaskbarPlacementCalculator.Calculate(
            new PhysicalRect(0, 984, 1920, 1080), 192);

        Assert.Equal(6, placement.LeftPhysicalPixel);
    }

    [Fact]
    public void Calculate_UsesTaskbarHeightMinusTwelveForRing()
    {
        var placement = TaskbarPlacementCalculator.Calculate(
            new PhysicalRect(0, 1032, 1920, 1080), 96);

        Assert.Equal(36, placement.RingDiameterDip);
    }

    [Fact]
    public void Calculate_UsesTwentyTwoDipRingFloorWhenTaskbarIsShorterThanThirtyFourDip()
    {
        var placement = TaskbarPlacementCalculator.Calculate(
            new PhysicalRect(0, 1050, 1920, 1080), 96);

        Assert.Equal(22, placement.RingDiameterDip);
    }

    [Fact]
    public void Calculate_HandlesNegativeMonitorCoordinatesInPhysicalPixels()
    {
        var rect = new PhysicalRect(-1920, -48, 0, 0);

        var placement = TaskbarPlacementCalculator.Calculate(rect, 96);

        Assert.Equal(-1914, placement.LeftPhysicalPixel);
        Assert.Equal(-48, placement.TopPhysicalPixel);
        Assert.Equal(-1746, placement.RightPhysicalPixel);
        Assert.Equal(0, placement.BottomPhysicalPixel);
    }

    [Fact]
    public void Calculate_NeverPlacesWindowOutsideNarrowTaskbar()
    {
        var rect = new PhysicalRect(-120, 1032, 0, 1080);

        var placement = TaskbarPlacementCalculator.Calculate(rect, 96);

        Assert.True(placement.LeftPhysicalPixel >= rect.Left);
        Assert.True(placement.RightPhysicalPixel <= rect.Right);
        Assert.True(placement.BottomPhysicalPixel <= rect.Bottom);
    }

    [Fact]
    public void Calculate_ClampsToZeroWidthWhenTaskbarIsNarrowerThanInset()
    {
        var rect = new PhysicalRect(10, 1032, 14, 1080);

        var placement = TaskbarPlacementCalculator.Calculate(rect, 96);

        Assert.Equal(14, placement.LeftPhysicalPixel);
        Assert.Equal(14, placement.RightPhysicalPixel);
        Assert.Equal(0, placement.WidthDip);
    }

    [Fact]
    public void Calculate_RejectsZeroDpi()
    {
        var rect = new PhysicalRect(0, 1032, 1920, 1080);

        Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarPlacementCalculator.Calculate(rect, 0));
    }

    [Theory]
    [InlineData(0, 0, 0, 48)]
    [InlineData(0, 48, 1920, 48)]
    public void Calculate_RejectsDegenerateTaskbarRectangles(int left, int top, int right, int bottom)
    {
        var rect = new PhysicalRect(left, top, right, bottom);

        Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarPlacementCalculator.Calculate(rect, 96));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void Calculate_RejectsNonPositiveDesiredWidth(double desiredWidthDip)
    {
        var rect = new PhysicalRect(0, 1032, 1920, 1080);

        Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarPlacementCalculator.Calculate(rect, 96, desiredWidthDip));
    }
}
