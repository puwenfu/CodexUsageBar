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
        Assert.Equal(new PhysicalRect(166, 2024, 374, 2090), placement.Bounds);
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
        Assert.Equal(new PhysicalRect(166, 2012, 374, 2078), placement.Bounds);
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
        Assert.Equal(new PhysicalRect(166, 2012, 374, 2078), placement.Bounds);
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
        Assert.Equal(new PhysicalRect(3412, 2012, 3620, 2078), placement.Bounds);
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
    [InlineData(96u, 849, 893)]
    [InlineData(144u, 824, 890)]
    [InlineData(192u, 799, 887)]
    public void AccountFooterBottomInset_ScalesWithDpi(
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
