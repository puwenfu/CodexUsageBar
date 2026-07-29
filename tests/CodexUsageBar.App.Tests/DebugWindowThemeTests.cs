using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.App.Views;

namespace CodexUsageBar.App.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class DebugWindowThemeTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void RightPlacement_LeavesTwoPhysicalPixelsBetweenVisiblePanels(double dpiScale)
    {
        var fromDevice = Matrix.Identity;
        fromDevice.Scale(1d / dpiScale, 1d / dpiScale);
        var anchorTopRightPixels = new Point(300d * dpiScale, 80d * dpiScale);

        var position = DebugWindow.CalculateRightPlacement(
            anchorTopRightPixels,
            fromDevice,
            new Size(220, 302),
            new Size(196, 278),
            new Rect(0, 0, 1920, 1080));

        var debugPanelLeftPixels = (position.X + 12d) * dpiScale;
        var debugPanelTopPixels = (position.Y + 12d) * dpiScale;
        Assert.Equal(anchorTopRightPixels.X + 2d, debugPanelLeftPixels, precision: 6);
        Assert.Equal(anchorTopRightPixels.Y, debugPanelTopPixels, precision: 6);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void RightPlacement_KeepsVisiblePanelOnePhysicalPixelAboveTaskbar(
        double dpiScale)
    {
        var fromDevice = Matrix.Identity;
        fromDevice.Scale(1d / dpiScale, 1d / dpiScale);
        var workArea = new Rect(0, 0, 1280, 680);
        var anchorTopRightPixels = new Point(300d * dpiScale, 640d * dpiScale);

        var position = DebugWindow.CalculateRightPlacement(
            anchorTopRightPixels,
            fromDevice,
            new Size(220, 302),
            new Size(196, 278),
            workArea);

        var debugPanelBottomPixels = (position.Y + 12d + 278d) * dpiScale;
        Assert.Equal(
            (workArea.Bottom * dpiScale) - 1d,
            debugPanelBottomPixels,
            precision: 6);
    }

    [Fact]
    public void DebugWindow_UsesMenuCardChromeAndThemedControls() => StaTest.Run(() =>
    {
        var window = new DebugWindow(new DebugViewModel());
        try
        {
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.AllowsTransparency);
            Assert.False(window.ShowInTaskbar);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(220d, window.Width);
            Assert.Equal(302d, window.Height);
            Assert.Equal(
                Colors.Transparent,
                Assert.IsType<SolidColorBrush>(window.Background).Color);
            Assert.Equal(196d, window.DebugPanel.Width);
            Assert.Equal(278d, window.DebugPanel.Height);
            Assert.Equal(new CornerRadius(11), window.DebugPanel.CornerRadius);
            Assert.IsType<DropShadowEffect>(window.DebugPanel.Effect);
            Assert.IsType<LinearGradientBrush>(window.DebugPanel.Background);
            Assert.Equal(31d, window.FiveHourTextBox.Height);
            Assert.Equal(
                VerticalAlignment.Center,
                window.FiveHourTextBox.VerticalContentAlignment);
            Assert.Equal(
                VerticalAlignment.Center,
                window.WeeklyTextBox.VerticalContentAlignment);
            Assert.Same(
                window.FindResource("DebugCheckBoxStyle"),
                window.SimulationToggle.Style);
            window.SimulationToggle.ApplyTemplate();
            var toggleTrack = Assert.IsType<Border>(
                window.SimulationToggle.Template.FindName(
                    "ToggleSwitch",
                    window.SimulationToggle));
            var toggleThumb = Assert.IsType<System.Windows.Shapes.Ellipse>(
                window.SimulationToggle.Template.FindName(
                    "ToggleThumb",
                    window.SimulationToggle));
            Assert.Equal(23.8d, toggleTrack.Width);
            Assert.Equal(13.6d, toggleTrack.Height);
            Assert.Equal(10d, toggleThumb.Width);
            Assert.IsType<DropShadowEffect>(toggleTrack.Effect);
            Assert.IsType<DropShadowEffect>(toggleThumb.Effect);
            Assert.Equal(HorizontalAlignment.Left, toggleThumb.HorizontalAlignment);

            window.SimulationToggle.IsChecked = true;
            Assert.Equal(
                Color.FromRgb(0xA8, 0xFF, 0x7A),
                Assert.IsType<SolidColorBrush>(toggleTrack.Background).Color);
            Assert.Equal(HorizontalAlignment.Right, toggleThumb.HorizontalAlignment);
            Assert.Same(
                window.FindResource("DebugSliderStyle"),
                window.FiveHourSlider.Style);
            Assert.Same(
                window.FindResource("DebugSliderStyle"),
                window.WeeklySlider.Style);
            Assert.Same(
                window.FindResource("CodexSliderStyle"),
                Assert.IsType<Style>(window.FindResource("DebugSliderStyle")).BasedOn);
            Assert.Same(
                window.FindResource("DebugTextBoxStyle"),
                window.FiveHourTextBox.Style);
            Assert.Same(
                window.FindResource("DebugTextBoxStyle"),
                window.WeeklyTextBox.Style);
            Assert.Equal("调试面板", window.DebugTitle.Text);
            Assert.Equal("×", Assert.IsType<string>(window.CloseButton.Content));
        }
        finally
        {
            window.Close();
        }
    });
}
