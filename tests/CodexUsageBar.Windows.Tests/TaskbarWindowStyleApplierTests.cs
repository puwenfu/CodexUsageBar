using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Taskbar;

namespace CodexUsageBar.Windows.Tests;

public sealed class TaskbarWindowStyleApplierTests
{
    [Fact]
    public void TryApply_SetsAndVerifiesRequiredStylesWhilePreservingUnrelatedBits()
    {
        var native = new FakeWindowsNativeApi { WindowParent = new nint(84) };

        var applied = TaskbarWindowStyleApplier.TryApply(native, (nint)42);

        Assert.True(applied);
        Assert.True(TaskbarWindowPolicy.HasRequiredExtendedStyles(native.WindowExtendedStyle));
        Assert.True(TaskbarWindowPolicy.HasRequiredWindowStyles(native.WindowStyle));
        Assert.Equal(0, native.WindowParent);
        Assert.All(native.WindowParentTargets, target => Assert.Equal(0, target));
        Assert.NotEqual(0, native.WindowExtendedStyle & 0x20L);
        Assert.NotEqual(0, native.WindowStyle & 0x1000L);
    }

    [Fact]
    public void TryApply_FailsClosedWhenStyleReadFails()
    {
        var native = new FakeWindowsNativeApi { GetWindowExtendedStyleSucceeds = false };

        Assert.False(TaskbarWindowStyleApplier.TryApply(native, (nint)42));
    }

    [Fact]
    public void TryApply_FailsClosedWhenStyleWriteFails()
    {
        var native = new FakeWindowsNativeApi { SetWindowExtendedStyleSucceeds = false };

        Assert.False(TaskbarWindowStyleApplier.TryApply(native, (nint)42));
    }

    [Fact]
    public void TryApply_FailsClosedWhenStyleReadbackDoesNotContainRequiredFlags()
    {
        var native = new FakeWindowsNativeApi { IgnoreWindowExtendedStyleWrites = true };

        Assert.False(TaskbarWindowStyleApplier.TryApply(native, (nint)42));
    }

    [Fact]
    public void TryApply_FailsClosedWhenDesktopParentCannotBeRestored()
    {
        var native = new FakeWindowsNativeApi { SetWindowParentSucceeds = false };

        Assert.False(TaskbarWindowStyleApplier.TryApply(native, (nint)42));
    }

    [Theory]
    [InlineData(0L, 0, true)]
    [InlineData(0L, 5, false)]
    [InlineData(123L, 5, true)]
    public void PointerResultSucceeded_DistinguishesLegitimateZeroFromFailure(long result, int lastError, bool expected)
    {
        Assert.Equal(expected, NativeCallResultPolicy.PointerResultSucceeded(new nint(result), lastError));
    }
}
