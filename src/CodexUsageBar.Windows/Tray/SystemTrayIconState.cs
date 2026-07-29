using System.Windows.Media;

namespace CodexUsageBar.Windows.Tray;

public readonly record struct SystemTrayIconState(
    double Progress,
    string Text,
    Color TextColor,
    Color TrackColor,
    Color GradientStartColor,
    Color GradientMiddleColor,
    Color GradientEndColor);
