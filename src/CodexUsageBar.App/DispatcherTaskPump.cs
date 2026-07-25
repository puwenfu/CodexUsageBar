using System.Windows.Threading;

namespace CodexUsageBar.App;

internal static class DispatcherTaskPump
{
    public static void Wait(Task task, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.VerifyAccess();
        if (task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        var frame = new DispatcherFrame();
        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask;
                try
                {
                    _ = dispatcher.BeginInvoke(
                        DispatcherPriority.Send,
                        () => frame.Continue = false);
                }
                catch (InvalidOperationException)
                {
                    frame.Continue = false;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
