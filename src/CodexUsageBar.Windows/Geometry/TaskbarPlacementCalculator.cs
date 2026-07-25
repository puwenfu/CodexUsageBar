namespace CodexUsageBar.Windows.Geometry;

public static class TaskbarPlacementCalculator
{
    private const int LeftInsetPhysicalPixels = 6;
    private const double MinimumRingDiameterDip = 22;
    private const double RingVerticalAllowanceDip = 12;

    public static TaskbarPlacement Calculate(
        PhysicalRect taskbarRect,
        uint dpi,
        double desiredWidthDip = 168)
    {
        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be greater than zero.");
        }

        if (taskbarRect.Width <= 0 || taskbarRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskbarRect), "Taskbar rectangle must have positive width and height.");
        }

        if (!double.IsFinite(desiredWidthDip) || desiredWidthDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredWidthDip), desiredWidthDip, "Desired width must be finite and greater than zero.");
        }

        var scale = dpi / 96d;
        var leftPhysicalPixel = ClampToRect(taskbarRect.Left + (long)LeftInsetPhysicalPixels, taskbarRect.Left, taskbarRect.Right);
        var topPhysicalPixel = taskbarRect.Top;
        var availableWidthDip = (taskbarRect.Right - leftPhysicalPixel) / scale;
        var widthDip = Math.Min(desiredWidthDip, Math.Max(0, availableWidthDip));
        var heightDip = taskbarRect.Height / scale;
        var leftDip = leftPhysicalPixel / scale;
        var topDip = topPhysicalPixel / scale;
        var ringDiameterDip = Math.Min(
            heightDip,
            Math.Max(MinimumRingDiameterDip, heightDip - RingVerticalAllowanceDip));

        var rightPhysicalPixel = ClampToRect(
            RoundToPhysicalPixel((leftDip + widthDip) * scale),
            leftPhysicalPixel,
            taskbarRect.Right);
        var bottomPhysicalPixel = ClampToRect(
            RoundToPhysicalPixel((topDip + heightDip) * scale),
            topPhysicalPixel,
            taskbarRect.Bottom);

        return new TaskbarPlacement(
            leftDip,
            topDip,
            widthDip,
            heightDip,
            ringDiameterDip,
            leftPhysicalPixel,
            topPhysicalPixel,
            rightPhysicalPixel,
            bottomPhysicalPixel);
    }

    private static int ClampToRect(long value, int minimum, int maximum) =>
        (int)Math.Clamp(value, (long)minimum, maximum);

    private static long RoundToPhysicalPixel(double value) =>
        checked((long)Math.Round(value, MidpointRounding.AwayFromZero));
}
