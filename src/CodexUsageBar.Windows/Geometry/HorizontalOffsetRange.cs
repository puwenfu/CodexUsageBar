namespace CodexUsageBar.Windows.Geometry;

public readonly record struct HorizontalOffsetRange(
    double MinimumDip,
    double MaximumDip)
{
    public double Clamp(double offsetDip)
    {
        if (!double.IsFinite(offsetDip))
        {
            return 0d;
        }

        return Math.Clamp(offsetDip, MinimumDip, MaximumDip);
    }
}
