using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Taskbar;

public sealed record TaskbarInfo(
    nint WindowHandle,
    PhysicalRect Rectangle,
    uint Dpi,
    PhysicalRect MonitorBounds,
    bool IsAutoHide);
