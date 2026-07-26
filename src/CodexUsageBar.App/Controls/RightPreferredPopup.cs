using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodexUsageBar.App.Controls;

public sealed class RightPreferredPopup : Popup
{
    public RightPreferredPopup()
    {
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = PlaceRightThenLeft;
        StaysOpen = true;
    }

    private CustomPopupPlacement[] PlaceRightThenLeft(
        Size popupSize,
        Size targetSize,
        Point _)
    {
        var dpiScale = PlacementTarget is Visual target
            ? VisualTreeHelper.GetDpi(target).DpiScaleX
            : 1d;
        var twoPhysicalPixelsDip = 2d / dpiScale;

        // Root menu chrome ends 3 DIPs beyond the item while submenu chrome
        // starts 2 DIPs inside its popup. These offsets leave two physical
        // pixels between the two visible borders at any DPI.
        var rightOffset = 1d + twoPhysicalPixelsDip;
        var leftOffset = 9d - twoPhysicalPixelsDip;

        return
        [
            new(
                new Point(targetSize.Width + rightOffset, 0),
                PopupPrimaryAxis.Vertical),
            new(
                new Point(-popupSize.Width + leftOffset, 0),
                PopupPrimaryAxis.Vertical),
        ];
    }
}
