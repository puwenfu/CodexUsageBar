namespace CodexUsageBar.Windows.Geometry;

public sealed record TaskbarPlacement(
    double LeftDip,
    double TopDip,
    double WidthDip,
    double HeightDip,
    double RingDiameterDip,
    int LeftPhysicalPixel,
    int TopPhysicalPixel,
    int RightPhysicalPixel,
    int BottomPhysicalPixel);
