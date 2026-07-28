using CodexUsageBar.App.Services;

namespace CodexUsageBar.App.Tests;

public sealed class WidgetPlacementPolicyTests
{
    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 2)]
    public void Automatic_UsesFullSizeTaskbarThenCodexThenTray(
        bool fullTaskbar,
        bool codexSidebar,
        int expected)
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.Automatic,
            new WidgetPlacementAvailability(
                fullTaskbar,
                codexSidebar));

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
                CodexSidebar: true));

        Assert.Equal((WidgetSurface)expected, actual);
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 2)]
    public void CodexPreferred_UsesCodexThenTaskbarThenTray(
        bool fullTaskbar,
        bool codexSidebar,
        int expected)
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.CodexSidebarPreferred,
            new WidgetPlacementAvailability(
                fullTaskbar,
                codexSidebar));

        Assert.Equal((WidgetSurface)expected, actual);
    }

    [Fact]
    public void TrayOnly_IgnoresOtherAvailability()
    {
        var actual = WidgetPlacementPolicy.Resolve(
            WidgetPlacementPreference.SystemTrayOnly,
            new WidgetPlacementAvailability(true, true));

        Assert.Equal(WidgetSurface.SystemTray, actual);
    }
}
