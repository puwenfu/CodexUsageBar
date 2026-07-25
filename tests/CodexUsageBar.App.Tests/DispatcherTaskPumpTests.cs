using System.Windows.Threading;

namespace CodexUsageBar.App.Tests;

public sealed class DispatcherTaskPumpTests
{
    [Fact]
    public void Wait_AllowsUiBoundCleanupToCompleteWithoutBlockingDispatcher() => StaTest.Run(
        () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackRan = false;
            var cleanup = Task.Run(async () =>
            {
                cleanupStarted.SetResult();
                await dispatcher.InvokeAsync(() => callbackRan = true);
            });
            cleanupStarted.Task.GetAwaiter().GetResult();

            DispatcherTaskPump.Wait(cleanup, dispatcher);

            Assert.True(callbackRan);
            Assert.True(cleanup.IsCompletedSuccessfully);
        },
        timeout: TimeSpan.FromSeconds(3));
}
