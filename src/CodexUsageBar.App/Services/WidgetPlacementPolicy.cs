namespace CodexUsageBar.App.Services;

internal enum WidgetSurface
{
    TaskbarFull,
    CodexSidebar,
    SystemTray,
}

internal readonly record struct WidgetPlacementAvailability(
    bool TaskbarFull,
    bool CodexSidebar);

internal static class WidgetPlacementPolicy
{
    internal static WidgetSurface Resolve(
        WidgetPlacementPreference preference,
        WidgetPlacementAvailability availability) =>
        preference switch
        {
            WidgetPlacementPreference.Automatic =>
                FirstAvailable(
                    availability.TaskbarFull,
                    WidgetSurface.TaskbarFull,
                    availability.CodexSidebar,
                    WidgetSurface.CodexSidebar),
            WidgetPlacementPreference.TaskbarPreferred =>
                availability.TaskbarFull
                    ? WidgetSurface.TaskbarFull
                    : WidgetSurface.SystemTray,
            WidgetPlacementPreference.CodexSidebarPreferred when availability.CodexSidebar =>
                WidgetSurface.CodexSidebar,
            WidgetPlacementPreference.CodexSidebarPreferred =>
                availability.TaskbarFull
                    ? WidgetSurface.TaskbarFull
                    : WidgetSurface.SystemTray,
            WidgetPlacementPreference.SystemTrayOnly => WidgetSurface.SystemTray,
            _ => WidgetSurface.SystemTray,
        };

    private static WidgetSurface FirstAvailable(
        bool primaryAvailable,
        WidgetSurface primary,
        bool secondaryAvailable,
        WidgetSurface secondary)
    {
        if (primaryAvailable)
        {
            return primary;
        }

        return secondaryAvailable ? secondary : WidgetSurface.SystemTray;
    }
}
