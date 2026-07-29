using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarAvailablePlacementFinderTests
{
    private static readonly TaskbarInfo Taskbar = new(
        new nint(10),
        new PhysicalRect(0, 2088, 3840, 2160),
        144,
        new PhysicalRect(0, 0, 3840, 2160),
        IsAutoHide: false);

    [Fact]
    public void FullPlacement_PrefersFreeSpaceBeforeCenteredTaskButtons()
    {
        var found = TaskbarAvailablePlacementFinder.TryCalculate(
            Taskbar,
            desiredWidthDip: 168,
            occupiedLeftPhysicalPixel: 900,
            occupiedRightPhysicalPixel: 2500,
            trayLeftPhysicalPixel: 3069,
            out var placement);

        Assert.True(found);
        Assert.Equal(6, placement.LeftPhysicalPixel);
        Assert.Equal(258, placement.RightPhysicalPixel);
    }

    [Fact]
    public void FullPlacement_UsesSpaceBeforeTrayWhenLeftSideIsOccupied()
    {
        var found = TaskbarAvailablePlacementFinder.TryCalculate(
            Taskbar,
            desiredWidthDip: 168,
            occupiedLeftPhysicalPixel: 200,
            occupiedRightPhysicalPixel: 2500,
            trayLeftPhysicalPixel: 3069,
            out var placement);

        Assert.True(found);
        Assert.Equal(2811, placement.LeftPhysicalPixel);
        Assert.Equal(3063, placement.RightPhysicalPixel);
    }

    [Fact]
    public void CrowdedTaskbar_RejectsBothFullAndCompactPlacement()
    {
        var fullFound = TaskbarAvailablePlacementFinder.TryCalculate(
            Taskbar,
            desiredWidthDip: 168,
            occupiedLeftPhysicalPixel: 200,
            occupiedRightPhysicalPixel: 2982,
            trayLeftPhysicalPixel: 3069,
            out _);
        var compactFound = TaskbarAvailablePlacementFinder.TryCalculate(
            Taskbar,
            desiredWidthDip: 112,
            occupiedLeftPhysicalPixel: 175,
            occupiedRightPhysicalPixel: 2982,
            trayLeftPhysicalPixel: 3069,
            out _);

        Assert.False(fullFound);
        Assert.False(compactFound);
    }

    [Theory]
    [InlineData(96u, 46)]
    [InlineData(144u, 66)]
    [InlineData(192u, 86)]
    public void HorizontalOffset_UsesDeviceIndependentPixelsAtEachDpi(
        uint dpi,
        int expectedLeft)
    {
        var taskbar = Taskbar with { Dpi = dpi };

        var found = TaskbarAvailablePlacementFinder.TryCalculate(
            taskbar,
            desiredWidthDip: 100,
            occupiedLeftPhysicalPixel: 900,
            occupiedRightPhysicalPixel: 2500,
            trayLeftPhysicalPixel: 3600,
            horizontalOffsetDip: 40,
            out var placement,
            out var range);

        Assert.True(found);
        Assert.Equal(expectedLeft, placement.LeftPhysicalPixel);
        Assert.Equal(0d, range.MinimumDip);
        Assert.True(range.MaximumDip > 40d);
    }

    [Fact]
    public void HorizontalOffset_ClampsInsideTaskbarFreeSpace()
    {
        var found = TaskbarAvailablePlacementFinder.TryCalculate(
            Taskbar,
            desiredWidthDip: 168,
            occupiedLeftPhysicalPixel: 900,
            occupiedRightPhysicalPixel: 2500,
            trayLeftPhysicalPixel: 3069,
            horizontalOffsetDip: 4096,
            out var placement,
            out var range);

        Assert.True(found);
        Assert.Equal(894, placement.RightPhysicalPixel);
        Assert.Equal(range.MaximumDip, range.Clamp(4096d));
    }
}
