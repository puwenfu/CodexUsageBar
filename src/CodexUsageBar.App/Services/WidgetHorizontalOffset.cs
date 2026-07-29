namespace CodexUsageBar.App.Services;

internal static class WidgetHorizontalOffset
{
    private const double MaximumMagnitudeDip = 4096d;

    public static double Normalize(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, -MaximumMagnitudeDip, MaximumMagnitudeDip)
            : 0d;
}
