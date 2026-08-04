using CodexUsageBar.App.Services;

namespace CodexUsageBar.App.Tests;

public sealed class WidgetPlacementPolicyTests
{
    [Theory]
    [InlineData(true, true, true, 0)]
    [InlineData(false, true, true, 1)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, false, false, 2)]
    public void Automatic_UsesTaskbarThenForegroundCodexThenTray(
        bool fullTaskbar,
        bool codexSidebar,
        bool codexAnchorForeground,
        int expected)
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.Automatic,
            new WidgetPlacementAvailability(
                fullTaskbar,
                codexSidebar,
                codexAnchorForeground));

        Assert.Equal((WidgetSurface)expected, actual);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    public void TaskbarPreferred_UsesFullSizeOrTray(
        bool fullTaskbar,
        int expected)
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.TaskbarPreferred,
            new WidgetPlacementAvailability(
                fullTaskbar,
                CodexSidebar: true,
                CodexAnchorForeground: true));

        Assert.Equal((WidgetSurface)expected, actual);
    }

    [Theory]
    [InlineData(true, true, true, 1)]
    [InlineData(true, true, false, 2)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, false, false, 2)]
    public void CodexPreferred_UsesCodexOnlyWhileAnchorIsForeground(
        bool fullTaskbar,
        bool codexSidebar,
        bool codexAnchorForeground,
        int expected)
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.CodexSidebarPreferred,
            new WidgetPlacementAvailability(
                fullTaskbar,
                codexSidebar,
                codexAnchorForeground));

        Assert.Equal((WidgetSurface)expected, actual);
    }

    [Fact]
    public void TrayOnly_IgnoresOtherAvailability()
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.SystemTrayOnly,
            new WidgetPlacementAvailability(
                TaskbarFull: true,
                CodexSidebar: true,
                CodexAnchorForeground: true));

        Assert.Equal(WidgetSurface.SystemTray, actual);
    }
}
