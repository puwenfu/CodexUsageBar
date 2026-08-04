using System.Diagnostics;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Codex;

public sealed class CodexSidebarPlacementFinder
{
    internal const double SidebarTargetLeftDip = 116d;
    internal const double SidebarTargetWidthDip = 416d / 3d;
    internal const int SidebarHorizontalNudgePhysicalPixels = -8;
    internal const double AccountFooterHeightDip = 44d;
    internal const double SidebarRightSafetyMarginDip = 0.5d;
    private const string CodexProcessName = "ChatGPT";
    private readonly IWindowsNativeApi _nativeApi;
    private readonly Func<IReadOnlyList<uint>> _processIdProvider;

    public string LastFailureReason { get; private set; } = string.Empty;

    public bool IsAnchorWindowForeground { get; private set; }

    public CodexSidebarPlacementFinder()
        : this(
            WindowsNativeApi.Instance,
            GetChatGptProcessIds)
    {
    }

    internal CodexSidebarPlacementFinder(IWindowsNativeApi nativeApi)
        : this(nativeApi, GetChatGptProcessIds)
    {
    }

    internal CodexSidebarPlacementFinder(
        IWindowsNativeApi nativeApi,
        Func<IReadOnlyList<uint>> processIdProvider)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        _processIdProvider = processIdProvider ??
            throw new ArgumentNullException(nameof(processIdProvider));
    }

    public bool TryFind(out CodexSidebarPlacement placement) =>
        TryFind(
            horizontalOffsetDip: 0d,
            out placement,
            out _);

    public bool TryFind(
        double horizontalOffsetDip,
        out CodexSidebarPlacement placement,
        out HorizontalOffsetRange horizontalOffsetRange)
    {
        placement = null!;
        horizontalOffsetRange = default;
        LastFailureReason = string.Empty;
        IsAnchorWindowForeground = false;
        if (!double.IsFinite(horizontalOffsetDip))
        {
            LastFailureReason = "InvalidHorizontalOffset";
            return false;
        }

        var processIds = _processIdProvider();
        if (processIds.Count == 0)
        {
            LastFailureReason = "CodexProcessUnavailable";
            return false;
        }

        CodexSidebarPlacement? bestPlacement = null;
        var bestHorizontalOffsetRange = default(HorizontalOffsetRange);
        long bestWindowArea = -1;
        var foundVisibleWindow = false;
        foreach (var processId in processIds)
        {
            foreach (var windowHandle in _nativeApi.EnumerateTopLevelWindows(processId))
            {
                if (!_nativeApi.IsWindow(windowHandle) ||
                    !_nativeApi.IsWindowVisibleAndNotMinimized(windowHandle) ||
                    _nativeApi.IsWindowCloaked(windowHandle) ||
                    !_nativeApi.TryGetWindowRectangle(
                        windowHandle,
                        out var outerWindowBounds))
                {
                    continue;
                }

                foundVisibleWindow = true;
                var placementBounds =
                    _nativeApi.TryGetWindowClientRectangle(
                        windowHandle,
                        out var clientBounds)
                        ? clientBounds
                        : outerWindowBounds;
                var dpi = _nativeApi.GetDpiForWindow(windowHandle);
                var sidebarRightPhysicalPixel =
                    GetConservativeSidebarRightBoundary(placementBounds, dpi);
                if (TryCalculate(
                        windowHandle,
                        placementBounds,
                        dpi,
                        sidebarRightPhysicalPixel,
                        horizontalOffsetDip,
                        out var candidatePlacement,
                        out var candidateHorizontalOffsetRange))
                {
                    var area = checked(
                        (long)outerWindowBounds.Width * outerWindowBounds.Height);
                    if (area > bestWindowArea)
                    {
                        bestPlacement = candidatePlacement;
                        bestHorizontalOffsetRange = candidateHorizontalOffsetRange;
                        bestWindowArea = area;
                    }
                }
            }
        }

        if (bestPlacement is null)
        {
            LastFailureReason = !foundVisibleWindow
                ? "VisibleCodexWindowUnavailable"
                : "SidebarSlotUnavailable";
            return false;
        }

        placement = bestPlacement;
        horizontalOffsetRange = bestHorizontalOffsetRange;
        IsAnchorWindowForeground =
            _nativeApi.GetForegroundWindow() == placement.AnchorWindowHandle;
        LastFailureReason = string.Empty;
        return true;
    }

    private static int GetConservativeSidebarRightBoundary(
        PhysicalRect windowBounds,
        uint dpi)
    {
        var scale = dpi / 96d;
        var minimumRequiredRightDip =
            SidebarTargetLeftDip +
            SidebarTargetWidthDip +
            SidebarRightSafetyMarginDip;
        return windowBounds.Left +
            checked((int)Math.Ceiling(minimumRequiredRightDip * scale));
    }

    private static IReadOnlyList<uint> GetChatGptProcessIds()
    {
        var processIds = new List<uint>();
        foreach (var process in Process.GetProcessesByName(CodexProcessName))
        {
            using (process)
            {
                try
                {
                    processIds.Add(checked((uint)process.Id));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while its identifier was being inspected.
                }
            }
        }

        return processIds;
    }

    internal static bool TryCalculate(
        nint ownerWindowHandle,
        PhysicalRect windowBounds,
        uint dpi,
        int sidebarRightPhysicalPixel,
        out CodexSidebarPlacement placement) =>
        TryCalculate(
            ownerWindowHandle,
            windowBounds,
            dpi,
            sidebarRightPhysicalPixel,
            horizontalOffsetDip: 0d,
            out placement,
            out _);

    internal static bool TryCalculate(
        nint ownerWindowHandle,
        PhysicalRect windowBounds,
        uint dpi,
        int sidebarRightPhysicalPixel,
        double horizontalOffsetDip,
        out CodexSidebarPlacement placement,
        out HorizontalOffsetRange horizontalOffsetRange)
    {
        placement = null!;
        horizontalOffsetRange = default;
        if (ownerWindowHandle == 0 ||
            dpi == 0 ||
            !double.IsFinite(horizontalOffsetDip) ||
            windowBounds.Width <= 0 ||
            windowBounds.Height <= 0)
        {
            return false;
        }

        var scale = dpi / 96d;
        if (windowBounds.Height / scale < AccountFooterHeightDip ||
            sidebarRightPhysicalPixel <= windowBounds.Left ||
            sidebarRightPhysicalPixel > windowBounds.Right)
        {
            return false;
        }

        var baseLeft = windowBounds.Left +
            checked((int)Math.Round(SidebarTargetLeftDip * scale)) +
            SidebarHorizontalNudgePhysicalPixels;
        var width = checked((int)Math.Round(SidebarTargetWidthDip * scale));
        var rightSafetyMargin = checked(
            (int)Math.Ceiling(SidebarRightSafetyMarginDip * scale));
        var minimumLeft = windowBounds.Left + rightSafetyMargin;
        var maximumDefaultLeft =
            sidebarRightPhysicalPixel - rightSafetyMargin - width;
        if (minimumLeft > maximumDefaultLeft ||
            baseLeft < minimumLeft ||
            baseLeft > maximumDefaultLeft)
        {
            return false;
        }

        var maximumLeft = windowBounds.Right - rightSafetyMargin - width;
        horizontalOffsetRange = new HorizontalOffsetRange(
            (minimumLeft - baseLeft) / scale,
            (maximumLeft - baseLeft) / scale);
        var appliedOffsetPhysicalPixels = checked(
            (int)Math.Round(horizontalOffsetRange.Clamp(horizontalOffsetDip) * scale));
        var left = baseLeft + appliedOffsetPhysicalPixels;

        var height = checked((int)Math.Round(AccountFooterHeightDip * scale));
        var bottom = windowBounds.Bottom;
        var top = bottom - height;
        placement = new CodexSidebarPlacement(
            ownerWindowHandle,
            new PhysicalRect(left, top, left + width, bottom),
            SidebarTargetWidthDip,
            AccountFooterHeightDip);
        return placement.Bounds.Right <= windowBounds.Right - rightSafetyMargin &&
            placement.Bounds.Top >= windowBounds.Top;
    }

}
