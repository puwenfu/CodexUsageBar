using CodexUsageBar.Windows.Codex;
using CodexUsageBar.Windows.Geometry;

namespace CodexUsageBar.Windows.Tests;

public sealed class CodexSidebarPlacementFinderTests
{
    [Fact]
    public void MultipleWindows_SelectsLargestVisibleChatGptWindow()
    {
        const uint processId = 29184;
        var narrowWindow = new nint(10);
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi { Dpi = 144 };
        nativeApi.TopLevelWindowsByProcessId[processId] = [narrowWindow, mainWindow];
        nativeApi.WindowRectangles[narrowWindow] = new PhysicalRect(3246, 1487, 3858, 2088);
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(0, 0, 3840, 2100);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out var placement);

        Assert.True(found);
        Assert.Equal(mainWindow, placement.AnchorWindowHandle);
        Assert.Equal(new PhysicalRect(166, 2034, 374, 2100), placement.Bounds);
    }

    [Fact]
    public void SingleVisibleWindow_UsesAvailableCodexSidebar()
    {
        const uint processId = 29184;
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi { Dpi = 144 };
        nativeApi.TopLevelWindowsByProcessId[processId] = [mainWindow];
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(0, 0, 1737, 2088);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out var placement);

        Assert.True(found);
        Assert.Equal(mainWindow, placement.AnchorWindowHandle);
        Assert.Equal(new PhysicalRect(166, 2022, 374, 2088), placement.Bounds);
    }

    [Fact]
    public void MaximizedWindow_UsesClientBoundsInsteadOfInvisibleResizeFrame()
    {
        const uint processId = 29184;
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi { Dpi = 144 };
        nativeApi.TopLevelWindowsByProcessId[processId] = [mainWindow];
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(-11, -11, 3852, 2099);
        nativeApi.WindowClientRectangles[mainWindow] = new PhysicalRect(-1, -1, 3840, 2089);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out var placement);

        Assert.True(found);
        Assert.Equal(new PhysicalRect(165, 2023, 373, 2089), placement.Bounds);
    }

    [Fact]
    public void WindowedWindow_PreservesExistingOuterMinusOnePlacement()
    {
        const uint processId = 29184;
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi { Dpi = 144 };
        nativeApi.TopLevelWindowsByProcessId[processId] = [mainWindow];
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(100, 100, 1837, 1659);
        nativeApi.WindowClientRectangles[mainWindow] = new PhysicalRect(100, 100, 1837, 1658);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out var placement);

        Assert.True(found);
        Assert.Equal(new PhysicalRect(266, 1592, 474, 1658), placement.Bounds);
    }

    [Fact]
    public void ClientBoundsUnavailable_FallsBackToOuterWindowBounds()
    {
        const uint processId = 29184;
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi
        {
            Dpi = 144,
            GetWindowClientRectangleSucceeds = false,
        };
        nativeApi.TopLevelWindowsByProcessId[processId] = [mainWindow];
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(0, 0, 1737, 2088);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out var placement);

        Assert.True(found);
        Assert.Equal(new PhysicalRect(166, 2022, 374, 2088), placement.Bounds);
    }

    [Fact]
    public void ForegroundMainWindow_IsReportedAsForegroundAnchor()
    {
        const uint processId = 29184;
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi
        {
            Dpi = 144,
            ForegroundWindowHandle = mainWindow,
        };
        nativeApi.TopLevelWindowsByProcessId[processId] = [mainWindow];
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(0, 0, 1737, 2088);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out _);

        Assert.True(found);
        Assert.True(finder.IsAnchorWindowForeground);
    }

    [Fact]
    public void DifferentForegroundWindow_IsReportedAsInactiveAnchor()
    {
        const uint processId = 29184;
        var mainWindow = new nint(20);
        var nativeApi = new FakeWindowsNativeApi
        {
            Dpi = 144,
            ForegroundWindowHandle = new nint(99),
        };
        nativeApi.TopLevelWindowsByProcessId[processId] = [mainWindow];
        nativeApi.WindowRectangles[mainWindow] = new PhysicalRect(0, 0, 1737, 2088);
        var finder = new CodexSidebarPlacementFinder(
            nativeApi,
            () => [processId]);

        var found = finder.TryFind(out _);

        Assert.True(found);
        Assert.False(finder.IsAnchorWindowForeground);
    }

    [Fact]
    public void ProductionAssembly_DoesNotReferenceUiAutomation()
    {
        var referencedAssemblies =
            typeof(CodexSidebarPlacementFinder).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            referencedAssemblies,
            assembly => assembly.Name?.StartsWith(
                "UIAutomation",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void WideCodexWindow_UsesApprovedAccountFooterSlotAtDpiScale()
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(0, 0, 1737, 2088),
            dpi: 144,
            sidebarRightPhysicalPixel: 500,
            out var placement);

        Assert.True(found);
        Assert.Equal(new PhysicalRect(166, 2022, 374, 2088), placement.Bounds);
        Assert.Equal(416d / 3d, placement.WidthDip, precision: 6);
        Assert.Equal(44d, placement.HeightDip);
    }

    [Fact]
    public void WiderAccountFooterSlot_AcceptsFullSizeWidget()
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(3246, 1487, 3858, 2088),
            dpi: 144,
            sidebarRightPhysicalPixel: 3750,
            out var placement);

        Assert.True(found);
        Assert.Equal(new PhysicalRect(3412, 2022, 3620, 2088), placement.Bounds);
    }

    [Fact]
    public void FormerCompactAccountFooterSlot_IsRejected()
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(0, 0, 1737, 2088),
            dpi: 144,
            sidebarRightPhysicalPixel: 350,
            out var placement);

        Assert.False(found);
    }

    [Theory]
    [InlineData(96u, 856, 900)]
    [InlineData(144u, 834, 900)]
    [InlineData(192u, 812, 900)]
    public void AccountFooterBottomEdge_MatchesClientBottomAtEveryDpi(
        uint dpi,
        int expectedTop,
        int expectedBottom)
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(0, 0, 1000, 900),
            dpi,
            sidebarRightPhysicalPixel: 600,
            out var placement);

        Assert.True(found);
        Assert.Equal(expectedTop, placement.Bounds.Top);
        Assert.Equal(expectedBottom, placement.Bounds.Bottom);
    }

    [Fact]
    public void WindowTooNarrowForTargetSlot_IsRejected()
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(0, 0, 330, 900),
            dpi: 144,
            sidebarRightPhysicalPixel: 320,
            out _);

        Assert.False(found);
    }

    [Theory]
    [InlineData(96u, 132)]
    [InlineData(144u, 202)]
    [InlineData(192u, 272)]
    public void HorizontalOffset_UsesDeviceIndependentPixelsAtEachDpi(
        uint dpi,
        int expectedLeft)
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(0, 0, 1400, 1000),
            dpi,
            sidebarRightPhysicalPixel: 900,
            horizontalOffsetDip: 24,
            out var placement,
            out var range);

        Assert.True(found);
        Assert.Equal(expectedLeft, placement.Bounds.Left);
        Assert.True(range.MinimumDip < 0d);
        Assert.True(range.MaximumDip > 24d);
    }

    [Fact]
    public void HorizontalOffset_ClampsInsideCodexWindow()
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(100, 0, 1600, 1000),
            dpi: 144,
            sidebarRightPhysicalPixel: 700,
            horizontalOffsetDip: -4096,
            out var placement,
            out var range);

        Assert.True(found);
        Assert.Equal(101, placement.Bounds.Left);
        Assert.Equal(range.MinimumDip, range.Clamp(-4096d));
    }

    [Fact]
    public void PositiveHorizontalOffset_CanMoveBeyondSidebarInsideCodexWindow()
    {
        var found = CodexSidebarPlacementFinder.TryCalculate(
            new nint(20),
            new PhysicalRect(0, 0, 1400, 1000),
            dpi: 144,
            sidebarRightPhysicalPixel: 500,
            horizontalOffsetDip: 300,
            out var placement,
            out var range);

        Assert.True(found);
        Assert.Equal(616, placement.Bounds.Left);
        Assert.True(placement.Bounds.Right > 500);
        Assert.True(placement.Bounds.Right < 1400);
        Assert.True(range.MaximumDip > 300d);
    }
}
