using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarWindowPolicyTests
{
    [Fact]
    public void ApplyExtendedStyles_MakesWindowNonActivatingToolWindowAndRemovesAppWindow()
    {
        var original = NativeMethods.WS_EX_APPWINDOW | 0x20L;

        var updated = TaskbarWindowPolicy.ApplyExtendedStyles(original);

        Assert.Equal(0, updated & NativeMethods.WS_EX_APPWINDOW);
        Assert.NotEqual(0, updated & NativeMethods.WS_EX_TOOLWINDOW);
        Assert.NotEqual(0, updated & NativeMethods.WS_EX_NOACTIVATE);
        Assert.NotEqual(0, updated & 0x20L);
    }

    [Fact]
    public void ApplyWindowStyles_MakesWindowATaskbarChildAndPreservesUnrelatedBits()
    {
        var original = NativeMethods.WS_POPUP | 0x1000L;

        var updated = TaskbarWindowPolicy.ApplyWindowStyles(original);

        Assert.Equal(0, updated & NativeMethods.WS_POPUP);
        Assert.NotEqual(0, updated & NativeMethods.WS_CHILD);
        Assert.NotEqual(0, updated & 0x1000L);
        Assert.True(TaskbarWindowPolicy.HasRequiredWindowStyles(updated));
    }

    [Fact]
    public void ToTaskbarClientBounds_ConvertsPhysicalScreenCoordinatesToParentCoordinates()
    {
        var placement = new Geometry.TaskbarPlacement(
            LeftDip: 4,
            TopDip: 1032,
            WidthDip: 168,
            HeightDip: 48,
            RingDiameterDip: 40,
            LeftPhysicalPixel: 4,
            TopPhysicalPixel: 1032,
            RightPhysicalPixel: 172,
            BottomPhysicalPixel: 1080);

        var clientBounds = TaskbarWindowPolicy.ToTaskbarClientBounds(
            placement,
            clientLeft: 7,
            clientTop: 3);

        Assert.Equal(new Geometry.PhysicalRect(7, 3, 175, 51), clientBounds);
    }

    [Theory]
    [InlineData(NativeMethods.WM_DPICHANGED)]
    [InlineData(NativeMethods.WM_DISPLAYCHANGE)]
    [InlineData(NativeMethods.WM_SETTINGCHANGE)]
    public void GetMessageAction_RelocatesImmediatelyForSystemGeometryChanges(int message)
    {
        Assert.Equal(
            TaskbarMessageAction.RelocateNow,
            TaskbarWindowPolicy.GetMessageAction(message, taskbarCreatedMessage: 0xC123));
    }

    [Fact]
    public void GetMessageAction_DelaysRelocationAfterExplorerTaskbarCreated()
    {
        const int taskbarCreated = 0xC123;

        Assert.Equal(
            TaskbarMessageAction.RelocateAfterExplorerRestart,
            TaskbarWindowPolicy.GetMessageAction(taskbarCreated, taskbarCreated));
    }

    [Fact]
    public void GetMessageAction_DoesNotTreatFailedMessageRegistrationAsTaskbarCreated()
    {
        Assert.Equal(
            TaskbarMessageAction.None,
            TaskbarWindowPolicy.GetMessageAction(message: 0, taskbarCreatedMessage: 0));
    }

    [Fact]
    public void GetMessageAction_PreventsMouseActivation()
    {
        Assert.Equal(
            TaskbarMessageAction.PreventActivation,
            TaskbarWindowPolicy.GetMessageAction(NativeMethods.WM_MOUSEACTIVATE, taskbarCreatedMessage: 0xC123));
        Assert.Equal(NativeMethods.MA_NOACTIVATE, TaskbarWindowPolicy.MouseActivateResult);
    }

    [Fact]
    public void PositionFlags_ShowWithoutActivationAndLeaveSizeAndPositionAvailable()
    {
        Assert.Equal(
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW,
            TaskbarWindowPolicy.PositionFlags);
        Assert.Equal(0u, TaskbarWindowPolicy.PositionFlags & NativeMethods.SWP_NOMOVE);
        Assert.Equal(0u, TaskbarWindowPolicy.PositionFlags & NativeMethods.SWP_NOSIZE);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, true, false)]
    public void ShouldRestoreWindowVisibility_OnlyRepairsAnExpectedVisibleUnhealthyWindow(
        bool expectedVisible,
        bool visibleAndNotMinimized,
        bool cloaked,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarWindowPolicy.ShouldRestoreWindowVisibility(
                expectedVisible,
                visibleAndNotMinimized,
                cloaked));
        Assert.Equal(TimeSpan.FromMilliseconds(250), TaskbarWindowPolicy.WindowRecoveryInterval);
    }

    [Fact]
    public void WindowRecoveryPositionFlags_ReassertTaskbarSiblingOrderWithoutMoveResizeOrActivation()
    {
        Assert.Equal(
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW,
            TaskbarWindowPolicy.WindowRecoveryPositionFlags);
    }

    [Theory]
    [InlineData(42, 42, 10, false)]
    [InlineData(42, 84, 1, false)]
    [InlineData(42, 84, 2, true)]
    [InlineData(42, 0, 2, false)]
    [InlineData(0, 84, 2, false)]
    public void ShouldRestartAfterWindowLoss_RequiresAStableReplacementTaskbar(
        long lostTaskbarHandle,
        long candidateTaskbarHandle,
        int consecutiveObservations,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarWindowPolicy.ShouldRestartAfterWindowLoss(
                new nint(lostTaskbarHandle),
                new nint(candidateTaskbarHandle),
                consecutiveObservations));
    }

    [Theory]
    [InlineData(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE, true)]
    [InlineData(NativeMethods.WS_EX_TOOLWINDOW, false)]
    [InlineData(NativeMethods.WS_EX_NOACTIVATE, false)]
    [InlineData(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_APPWINDOW, false)]
    public void HasRequiredExtendedStyles_RequiresBothNonActivatingFlagsAndNoAppWindow(long styles, bool expected)
    {
        Assert.Equal(expected, TaskbarWindowPolicy.HasRequiredExtendedStyles(styles));
    }
}
