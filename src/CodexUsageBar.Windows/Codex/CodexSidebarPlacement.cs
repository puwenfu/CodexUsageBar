using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Codex;

public sealed record CodexSidebarPlacement(
    nint AnchorWindowHandle,
    PhysicalRect Bounds,
    double WidthDip,
    double HeightDip);
