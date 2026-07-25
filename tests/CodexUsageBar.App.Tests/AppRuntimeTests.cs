using CodexUsageBar.App.Diagnostics;

namespace CodexUsageBar.App.Tests;

public sealed class AppRuntimeTests
{
    [Fact]
    public async Task DisposeAsync_ReentrantCallsShareCleanup_AndExceptionsCannotSkipLaterResources()
    {
        var calls = new List<string>();
        var coordinator = new RecordingAsyncDisposable(calls, "coordinator", shouldThrow: true);
        var host = new RecordingDisposable(calls, "host", shouldThrow: true);
        var logger = new RecordingLogger(calls, shouldThrow: true);
        var guard = new RecordingDisposable(calls, "guard", shouldThrow: true);
        var runtime = new AppRuntime(coordinator, host, logger, guard, () => calls.Add("window"));

        var disposals = Enumerable.Range(0, 12)
            .Select(_ => runtime.DisposeAsync().AsTask())
            .ToArray();
        await Task.WhenAll(disposals).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(["coordinator", "host", "window", "logger", "guard"], calls);
        Assert.All(disposals, task => Assert.True(task.IsCompletedSuccessfully));
    }

    [Fact]
    public async Task DisposeAsync_ReleasesUiResourcesBeforeWaitingForAsyncClientCleanup()
    {
        var calls = new List<string>();
        var releaseCoordinator = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new GatedAsyncDisposable(calls, releaseCoordinator.Task);
        var runtime = new AppRuntime(
            coordinator,
            new RecordingDisposable(calls, "host"),
            new RecordingLogger(calls, shouldThrow: false),
            new RecordingDisposable(calls, "guard"),
            () => calls.Add("window"));

        var disposal = runtime.DisposeAsync().AsTask();

        Assert.Equal(["coordinator", "host", "window"], calls);
        Assert.False(disposal.IsCompleted);
        releaseCoordinator.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["coordinator", "host", "window", "logger", "guard"], calls);
    }

    [Fact]
    public async Task StartupResourceScope_WhenConstructionFails_DisposesEveryCreatedResourceInReverseOrder()
    {
        var calls = new List<string>();
        await using var scope = new StartupResourceScope();
        scope.Own(new RecordingDisposable(calls, "guard"));
        scope.Own(new RecordingAsyncDisposable(calls, "client"));
        scope.Own(new RecordingDisposable(calls, "logger"));

        await scope.DisposeAsync();

        Assert.Equal(["logger", "client", "guard"], calls);
    }

    [Fact]
    public async Task StartupResourceScope_ForgetTransfersOwnershipWithoutDoubleDisposal()
    {
        var calls = new List<string>();
        var client = new RecordingAsyncDisposable(calls, "client");
        await using var scope = new StartupResourceScope();
        scope.Own(client);
        scope.Forget(client);

        await scope.DisposeAsync();
        await client.DisposeAsync();

        Assert.Equal(["client"], calls);
    }

    private sealed class RecordingAsyncDisposable(
        List<string> calls,
        string name,
        bool shouldThrow = false) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                calls.Add(name);
                if (shouldThrow)
                {
                    return ValueTask.FromException(new InvalidOperationException(name));
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class GatedAsyncDisposable(List<string> calls, Task release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            calls.Add("coordinator");
            await release;
        }
    }

    private sealed class RecordingDisposable(
        List<string> calls,
        string name,
        bool shouldThrow = false) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                calls.Add(name);
                if (shouldThrow)
                {
                    throw new InvalidOperationException(name);
                }
            }
        }
    }

    private sealed class RecordingLogger(List<string> calls, bool shouldThrow) : IDiagnosticLogger
    {
        private int _disposed;

        public void Write(DiagnosticEvent diagnosticEvent, Exception? exception = null)
        {
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                calls.Add("logger");
                if (shouldThrow)
                {
                    throw new InvalidOperationException("logger");
                }
            }
        }
    }
}
