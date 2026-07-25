using System.Windows;
using System.Windows.Controls.Primitives;

namespace CodexUsageBar.App.Controls;

public sealed class RightPreferredPopup : Popup
{
    public RightPreferredPopup()
    {
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = PlaceRightThenLeft;
        StaysOpen = true;
    }

    private static CustomPopupPlacement[] PlaceRightThenLeft(
        Size popupSize,
        Size targetSize,
        Point _) =>
    [
        new(
            new Point(targetSize.Width, 0),
            PopupPrimaryAxis.Vertical),
        new(
            new Point(-popupSize.Width, 0),
            PopupPrimaryAxis.Vertical),
    ];
}
