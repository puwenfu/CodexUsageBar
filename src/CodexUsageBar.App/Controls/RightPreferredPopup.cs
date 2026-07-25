using System.Windows.Controls.Primitives;

namespace CodexUsageBar.App.Controls;

public sealed class RightPreferredPopup : Popup
{
    public RightPreferredPopup()
    {
        Placement = PlacementMode.Right;
        StaysOpen = true;
    }
}
