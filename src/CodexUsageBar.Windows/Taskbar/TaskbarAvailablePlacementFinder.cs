using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Taskbar;

public sealed class TaskbarAvailablePlacementFinder
{
    private const int EdgeGapPhysicalPixels = 6;
    private readonly TaskbarLocator _taskbarLocator;

    public TaskbarAvailablePlacementFinder()
        : this(new TaskbarLocator())
    {
    }

    internal TaskbarAvailablePlacementFinder(TaskbarLocator taskbarLocator) =>
        _taskbarLocator = taskbarLocator ?? throw new ArgumentNullException(nameof(taskbarLocator));

    public bool TryFind(double desiredWidthDip, out TaskbarPlacement placement) =>
        TryFind(
            desiredWidthDip,
            horizontalOffsetDip: 0d,
            out placement,
            out _);

    public bool TryFind(
        double desiredWidthDip,
        double horizontalOffsetDip,
        out TaskbarPlacement placement,
        out HorizontalOffsetRange horizontalOffsetRange)
    {
        placement = null!;
        horizontalOffsetRange = default;
        if (!_taskbarLocator.TryGetPrimary(out var taskbar))
        {
            return false;
        }

        return TryCalculate(
            taskbar,
            desiredWidthDip,
            taskbar.Rectangle.Right,
            taskbar.Rectangle.Left,
            taskbar.Rectangle.Right,
            horizontalOffsetDip,
            out placement,
            out horizontalOffsetRange);
    }

    internal static bool TryCalculate(
        TaskbarInfo taskbar,
        double desiredWidthDip,
        int occupiedLeftPhysicalPixel,
        int occupiedRightPhysicalPixel,
        int trayLeftPhysicalPixel,
        out TaskbarPlacement placement) =>
        TryCalculate(
            taskbar,
            desiredWidthDip,
            occupiedLeftPhysicalPixel,
            occupiedRightPhysicalPixel,
            trayLeftPhysicalPixel,
            horizontalOffsetDip: 0d,
            out placement,
            out _);

    internal static bool TryCalculate(
        TaskbarInfo taskbar,
        double desiredWidthDip,
        int occupiedLeftPhysicalPixel,
        int occupiedRightPhysicalPixel,
        int trayLeftPhysicalPixel,
        double horizontalOffsetDip,
        out TaskbarPlacement placement,
        out HorizontalOffsetRange horizontalOffsetRange)
    {
        placement = null!;
        horizontalOffsetRange = default;
        if (!double.IsFinite(desiredWidthDip) ||
            desiredWidthDip <= 0 ||
            !double.IsFinite(horizontalOffsetDip) ||
            taskbar.Dpi == 0)
        {
            return false;
        }

        var widthPhysicalPixels = checked((int)Math.Ceiling(desiredWidthDip * taskbar.Dpi / 96d));
        var scale = taskbar.Dpi / 96d;
        var minimumLeftPhysicalPixel = taskbar.Rectangle.Left + EdgeGapPhysicalPixels;
        var maximumLeftPhysicalPixel =
            occupiedLeftPhysicalPixel - EdgeGapPhysicalPixels - widthPhysicalPixels;
        if (minimumLeftPhysicalPixel <= maximumLeftPhysicalPixel)
        {
            horizontalOffsetRange = CreateOffsetRange(
                minimumLeftPhysicalPixel,
                minimumLeftPhysicalPixel,
                maximumLeftPhysicalPixel,
                scale);
            var availableLeftPhysicalPixel = ApplyOffset(
                minimumLeftPhysicalPixel,
                horizontalOffsetDip,
                horizontalOffsetRange,
                scale);
            placement = TaskbarPlacementCalculator.CalculateAtPhysicalLeft(
                taskbar.Rectangle,
                taskbar.Dpi,
                availableLeftPhysicalPixel,
                desiredWidthDip);
            return true;
        }

        var baseLeftPhysicalPixel = trayLeftPhysicalPixel -
            EdgeGapPhysicalPixels -
            widthPhysicalPixels;
        minimumLeftPhysicalPixel = Math.Max(
            occupiedRightPhysicalPixel + EdgeGapPhysicalPixels,
            taskbar.Rectangle.Left);
        maximumLeftPhysicalPixel = Math.Min(
            baseLeftPhysicalPixel,
            taskbar.Rectangle.Right - widthPhysicalPixels);
        if (minimumLeftPhysicalPixel > maximumLeftPhysicalPixel)
        {
            return false;
        }

        horizontalOffsetRange = CreateOffsetRange(
            baseLeftPhysicalPixel,
            minimumLeftPhysicalPixel,
            maximumLeftPhysicalPixel,
            scale);
        var leftPhysicalPixel = ApplyOffset(
            baseLeftPhysicalPixel,
            horizontalOffsetDip,
            horizontalOffsetRange,
            scale);
        placement = TaskbarPlacementCalculator.CalculateAtPhysicalLeft(
            taskbar.Rectangle,
            taskbar.Dpi,
            leftPhysicalPixel,
            desiredWidthDip);
        return placement.RightPhysicalPixel <= trayLeftPhysicalPixel - EdgeGapPhysicalPixels;
    }

    private static HorizontalOffsetRange CreateOffsetRange(
        int baseLeftPhysicalPixel,
        int minimumLeftPhysicalPixel,
        int maximumLeftPhysicalPixel,
        double scale) =>
        new(
            (minimumLeftPhysicalPixel - baseLeftPhysicalPixel) / scale,
            (maximumLeftPhysicalPixel - baseLeftPhysicalPixel) / scale);

    private static int ApplyOffset(
        int baseLeftPhysicalPixel,
        double horizontalOffsetDip,
        HorizontalOffsetRange horizontalOffsetRange,
        double scale) =>
        baseLeftPhysicalPixel +
        checked((int)Math.Round(horizontalOffsetRange.Clamp(horizontalOffsetDip) * scale));

}
