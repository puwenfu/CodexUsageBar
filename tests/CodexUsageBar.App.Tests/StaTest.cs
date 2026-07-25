using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace CodexUsageBar.App.Tests;

internal static class StaTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static void Run(Action action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (!thread.Join(effectiveTimeout))
        {
            throw new TimeoutException(
                $"STA test action did not complete within {effectiveTimeout.TotalMilliseconds:0} ms " +
                $"(thread id {thread.ManagedThreadId}, state {thread.ThreadState}).");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
