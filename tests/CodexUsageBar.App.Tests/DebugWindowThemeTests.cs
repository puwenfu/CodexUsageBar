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
            Assert.Equal(304d, window.Width);
            Assert.Equal(302d, window.Height);
            Assert.Equal(
                Colors.Transparent,
                Assert.IsType<SolidColorBrush>(window.Background).Color);
            Assert.Equal(280d, window.DebugPanel.Width);
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
            Assert.Same(
                window.FindResource("DebugSliderStyle"),
                window.FiveHourSlider.Style);
            Assert.Same(
                window.FindResource("DebugSliderStyle"),
                window.WeeklySlider.Style);
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
