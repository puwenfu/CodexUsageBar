using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CodexUsageBar.App.ViewModels;

namespace CodexUsageBar.App.Views;

public partial class DebugWindow : Window
{
    public DebugWindow(DebugViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    internal void PositionRightOf(FrameworkElement anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.ActualWidth <= 0 ||
            PresentationSource.FromVisual(anchor)?.CompositionTarget is not { } compositionTarget)
        {
            return;
        }

        var anchorTopRightPixels = anchor.PointToScreen(new Point(anchor.ActualWidth, 0));
        var position = CalculateRightPlacement(
            anchorTopRightPixels,
            compositionTarget.TransformFromDevice,
            new Size(Width, Height),
            new Size(DebugPanel.Width, DebugPanel.Height),
            SystemParameters.WorkArea);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = position.X;
        Top = position.Y;
    }

    internal static Point CalculateRightPlacement(
        Point anchorTopRightPixels,
        Matrix transformFromDevice,
        Size windowSize,
        Size visiblePanelSize,
        Rect workArea)
    {
        var panelTopLeftDip = transformFromDevice.Transform(
            new Point(anchorTopRightPixels.X + 2, anchorTopRightPixels.Y));
        var onePhysicalPixelDip = Math.Abs(transformFromDevice.M22);
        if (!double.IsFinite(onePhysicalPixelDip) || onePhysicalPixelDip <= 0)
        {
            onePhysicalPixelDip = 1d;
        }

        var minimumPanelTop = workArea.Top + onePhysicalPixelDip;
        var maximumPanelTop =
            workArea.Bottom - visiblePanelSize.Height - onePhysicalPixelDip;
        var constrainedPanelTop = maximumPanelTop >= minimumPanelTop
            ? Math.Clamp(panelTopLeftDip.Y, minimumPanelTop, maximumPanelTop)
            : minimumPanelTop;

        return new Point(
            panelTopLeftDip.X - ((windowSize.Width - visiblePanelSize.Width) / 2d),
            constrainedPanelTop - ((windowSize.Height - visiblePanelSize.Height) / 2d));
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Close();
        }
    }
}
