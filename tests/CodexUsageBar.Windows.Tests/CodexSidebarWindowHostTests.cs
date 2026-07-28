using System.Windows;
using CodexUsageBar.Windows.Codex;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Interop;

namespace CodexUsageBar.Windows.Tests;

public sealed class CodexSidebarWindowHostTests
{
    [Fact]
    public void ActivateAndDeactivate_UseIndependentNonActivatingZOrder()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var nativeApi = new FakeWindowsNativeApi();
                    nativeApi.WindowAboveByWindow[new nint(99)] = new nint(77);
                    var window = new Window
                    {
                        Opacity = 0,
                        ShowActivated = false,
                    };
                    using var host = new CodexSidebarWindowHost(nativeApi);
                    host.Attach(window);

                    var activated = host.Activate(
                        new CodexSidebarPlacement(
                            new nint(99),
                            new PhysicalRect(100, 200, 212, 244),
                            112,
                            44));

                    Assert.True(activated);
                    Assert.Equal(new nint(77), nativeApi.LastWindowInsertAfter);
                    Assert.Equal(0, nativeApi.WindowParent);
                    Assert.DoesNotContain(new nint(99), nativeApi.WindowParentTargets);

                    host.Deactivate();

                    Assert.Equal(NativeMethods.HWND_NOTOPMOST, nativeApi.LastWindowInsertAfter);
                    window.Close();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test thread did not stop.");
        Assert.Null(failure);
    }

    [Fact]
    public void RepeatedActivationForSameOwner_RelocatesWithoutHidingWindow()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var nativeApi = new FakeWindowsNativeApi();
                    var window = new Window
                    {
                        Opacity = 0,
                        ShowActivated = false,
                    };
                    var hiddenAfterActivation = 0;
                    using var host = new CodexSidebarWindowHost(nativeApi);
                    host.Attach(window);
                    Assert.True(host.Activate(
                        new CodexSidebarPlacement(
                            new nint(99),
                            new PhysicalRect(100, 200, 212, 244),
                            112,
                            44)));
                    window.IsVisibleChanged += (_, _) =>
                    {
                        if (!window.IsVisible)
                        {
                            hiddenAfterActivation++;
                        }
                    };

                    var relocated = host.Activate(
                        new CodexSidebarPlacement(
                            new nint(99),
                            new PhysicalRect(140, 200, 252, 244),
                            112,
                            44));

                    Assert.True(relocated);
                    Assert.Equal(0, hiddenAfterActivation);
                    Assert.Equal(
                        new PhysicalRect(140, 200, 252, 244),
                        nativeApi.LastWindowPosition);
                    window.Close();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test thread did not stop.");
        Assert.Null(failure);
    }
}
