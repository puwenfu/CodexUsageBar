using System.Diagnostics;
using System.Runtime.CompilerServices;
using CodexUsageBar.CodexProtocol.JsonRpc;
using CodexUsageBar.CodexProtocol.Transport;
using CodexUsageBar.FakeAppServer;

namespace CodexUsageBar.CodexProtocol.Tests;

public sealed class JsonRpcConnectionTests
{
    [Fact]
    public async Task RequestAsync_MatchesResponseByIdWhenNotificationArrivesFirst()
    {
        await using var transport = FakeServer.Start("notification-before-response");
        await using var connection = new JsonRpcConnection(transport);
        var notification = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, e) => notification.TrySetResult(e.Method);

        var result = await connection.RequestAsync(
            "initialize",
            new { clientInfo = new { name = "CodexUsageBar", version = "0.1.0" } },
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal("windows", result.GetProperty("platformOs").GetString());
        Assert.Equal(
            "account/rateLimits/updated",
            await notification.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task RequestAsync_DrainsBufferedResponseBeforeApplyingProcessExit()
    {
        await using var transport = new ExitBeforeBufferedResponseTransport();
        await using var connection = new JsonRpcConnection(transport);

        var result = await connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal("buffered", result.GetProperty("source").GetString());
    }

    [Fact]
    public async Task RequestAsync_IsCanceledWhenDisposeRacesWithWriteAdmission()
    {
        var transport = new DisposeDuringWriteTransport();
        var connection = new JsonRpcConnection(transport);
        var request = connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        await transport.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task RequestAsync_TimeoutIncludesBlockedWrite()
    {
        var transport = new BlockingWriteTransport();
        var connection = new JsonRpcConnection(transport);
        var stopwatch = Stopwatch.StartNew();
        var request = connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        try
        {
            await transport.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            Assert.True(request.IsCompleted, "The request timeout did not cover the blocked write.");
            await Assert.ThrowsAsync<TimeoutException>(() => request);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestAsync_CallerCancellationDuringWriteIsNotReportedAsTimeout()
    {
        await using var transport = new BlockingWriteTransport();
        await using var connection = new JsonRpcConnection(transport);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(2),
            cancellation.Token));
    }

    [Fact]
    public async Task RequestAsync_ResponseIsNotBlockedBySlowNotificationHandler()
    {
        var transport = FakeServer.Start("notification-before-response");
        var connection = new JsonRpcConnection(transport);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, _) =>
        {
            handlerStarted.TrySetResult();
            releaseHandler.Task.GetAwaiter().GetResult();
        };

        var request = connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        try
        {
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var completed = await Task.WhenAny(request, Task.Delay(TimeSpan.FromMilliseconds(300)));
            Assert.Same(request, completed);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await connection.DisposeAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task NotificationHandler_CanSynchronouslyRequestWithoutBlockingReader()
    {
        var transport = new ReentrantNotificationTransport();
        var connection = new JsonRpcConnection(transport);
        var nestedResult = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, _) =>
        {
            try
            {
                var result = connection.RequestAsync(
                    "nested",
                    null,
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None).GetAwaiter().GetResult();
                nestedResult.TrySetResult(result.GetProperty("source").GetString()!);
            }
            catch (Exception exception)
            {
                nestedResult.TrySetException(exception);
            }
        };

        var outerRequest = connection.RequestAsync(
            "outer",
            null,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        try
        {
            var completed = await Task.WhenAny(
                nestedResult.Task,
                Task.Delay(TimeSpan.FromMilliseconds(300)));
            Assert.Same(nestedResult.Task, completed);
            Assert.Equal("nested", await nestedResult.Task);
            Assert.Equal("outer", (await outerRequest).GetProperty("source").GetString());
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task NotificationHandler_ExceptionDoesNotStopResponseReaderOrEscapeDisposal()
    {
        await using var transport = FakeServer.Start("notification-before-response");
        await using var connection = new JsonRpcConnection(transport);
        connection.NotificationReceived += (_, _) =>
            throw new InvalidOperationException("sensitive notification detail");

        var result = await connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal("windows", result.GetProperty("platformOs").GetString());
    }

    [Fact]
    public async Task NotificationHandler_DisposeAsyncFailsFastInsteadOfSelfWaiting()
    {
        var transport = FakeServer.Start("notification-before-response");
        var connection = new JsonRpcConnection(transport);
        var callbackOutcome = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? callbackDispose = null;
        connection.NotificationReceived += (_, _) =>
        {
            callbackDispose = connection.DisposeAsync().AsTask();
            callbackOutcome.TrySetResult(
                callbackDispose.IsFaulted ? callbackDispose.Exception!.GetBaseException() : null);
        };
        var request = connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        try
        {
            var exception = await callbackOutcome.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsType<InvalidOperationException>(exception);
        }
        finally
        {
            if (callbackDispose is not null)
            {
                try
                {
                    await callbackDispose;
                }
                catch (InvalidOperationException)
                {
                }
            }

            await connection.DisposeAsync();
            await transport.DisposeAsync();
            try
            {
                await request;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public async Task TransportExitedHandler_DisposeAsyncFailsFastInsteadOfSelfWaiting()
    {
        var transport = FakeServer.Start("exit-before-response");
        var callbackOutcome = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? callbackDispose = null;
        transport.Exited += (_, _) =>
        {
            callbackDispose = transport.DisposeAsync().AsTask();
            callbackOutcome.TrySetResult(
                callbackDispose.IsFaulted ? callbackDispose.Exception!.GetBaseException() : null);
        };

        try
        {
            await transport.WriteLineAsync(
                "{\"id\":1,\"method\":\"initialize\",\"params\":null}",
                CancellationToken.None);
            var exception = await callbackOutcome.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsType<InvalidOperationException>(exception);
        }
        finally
        {
            if (callbackDispose is not null)
            {
                try
                {
                    await callbackDispose;
                }
                catch (InvalidOperationException)
                {
                }
            }

            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConnectionDisposeAsync_ConcurrentCallersAwaitSameCompleteCleanup()
    {
        var transport = new BlockingDisposeTransport();
        var connection = new JsonRpcConnection(transport);
        var pending = connection.RequestAsync(
            "initialize",
            null,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        await transport.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var firstDispose = connection.DisposeAsync().AsTask();
        Task? secondDispose = null;

        try
        {
            await transport.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            secondDispose = connection.DisposeAsync().AsTask();
            Assert.Same(firstDispose, secondDispose);
            Assert.False(secondDispose.IsCompleted);
        }
        finally
        {
            transport.ReleaseDispose.TrySetResult();
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(2));
            if (secondDispose is not null)
            {
                await secondDispose.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(transport.ReaderExited.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ProcessTransportDisposeAsync_ConcurrentCallersAwaitSameProcessExit()
    {
        var transport = FakeServer.Start("ready-then-hang");
        var exited = ObserveExit(transport);
        var exitHandlerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExitHandler = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Exited += (_, _) =>
        {
            exitHandlerEntered.TrySetResult();
            releaseExitHandler.Task.GetAwaiter().GetResult();
        };
        var firstDispose = Task.Run(async () => await transport.DisposeAsync());
        Task? secondDispose = null;

        try
        {
            await exitHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            secondDispose = transport.DisposeAsync().AsTask();
            Assert.False(secondDispose.IsCompleted);
        }
        finally
        {
            releaseExitHandler.TrySetResult();
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(2));
            if (secondDispose is not null)
            {
                await secondDispose.WaitAsync(TimeSpan.FromSeconds(2));
            }
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestAsync_TimesOutAndDisposalStopsChildWithinThreeSeconds()
    {
        var transport = FakeServer.Start("timeout");
        var exited = ObserveExit(transport);
        var connection = new JsonRpcConnection(transport);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => connection.RequestAsync(
                "initialize",
                null,
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None));
        }
        finally
        {
            await connection.DisposeAsync();
            await transport.DisposeAsync();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RequestAsync_ThrowsProcessExitedAndLeavesNoChildWithinThreeSeconds()
    {
        var transport = FakeServer.Start("exit-before-response");
        var exited = ObserveExit(transport);
        var connection = new JsonRpcConnection(transport);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Assert.ThrowsAsync<CodexProcessExitedException>(() => connection.RequestAsync(
                "initialize",
                null,
                TimeSpan.FromSeconds(2),
                CancellationToken.None));
        }
        finally
        {
            await connection.DisposeAsync();
            await transport.DisposeAsync();
        }

        Assert.Equal(17, await exited.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RequestAsync_TranslatesRpcErrorWithoutExposingServerPayload()
    {
        await using var transport = FakeServer.Start("rpc-error");
        await using var connection = new JsonRpcConnection(transport);

        var exception = await Assert.ThrowsAsync<CodexJsonRpcException>(() =>
            connection.RequestAsync("missing", null, TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.DoesNotContain("sensitive fake detail", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotifyAsync_WritesOneCompactJsonLineWithoutRequestId()
    {
        await using var transport = FakeServer.Start("notification-write");
        await using var connection = new JsonRpcConnection(transport);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, e) =>
        {
            if (e.Method == "fake/accepted")
            {
                accepted.TrySetResult();
            }
        };

        await connection.NotifyAsync("initialized", new { ready = true }, CancellationToken.None);

        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisposeAsync_CancelsPendingRequestAndStopsChildWithinThreeSeconds()
    {
        var transport = FakeServer.Start("ready-then-hang");
        var exited = ObserveExit(transport);
        var connection = new JsonRpcConnection(transport);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, e) =>
        {
            if (e.Method == "fake/ready")
            {
                ready.TrySetResult();
            }
        };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var pending = connection.RequestAsync(
                "initialize",
                null,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await connection.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            await connection.DisposeAsync();
            await transport.DisposeAsync();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Start_TranslatesMissingExecutable()
    {
        var exception = Assert.Throws<CodexCommandNotFoundException>(() =>
            ProcessJsonLineTransport.Start(new AppServerCommand(
                $"missing-codex-{Guid.NewGuid():N}",
                Array.Empty<string>())));

        Assert.Null(exception.InnerException);
    }

    private static TaskCompletionSource<int?> ObserveExit(IJsonLineTransport transport)
    {
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Exited += (_, exitCode) => exited.TrySetResult(exitCode);
        return exited;
    }

    private static class FakeServer
    {
        public static ProcessJsonLineTransport Start(string scenario) =>
            ProcessJsonLineTransport.Start(new AppServerCommand(
                "dotnet",
                [typeof(FakeAppServerMarker).Assembly.Location, scenario]));
    }

    private sealed class ExitBeforeBufferedResponseTransport : IJsonLineTransport
    {
        private readonly TaskCompletionSource _written =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<int?>? Exited;

        public async IAsyncEnumerable<string> ReadLinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await _written.Task.WaitAsync(cancellationToken);
            Exited?.Invoke(this, 0);
            yield return "{\"id\":1,\"result\":{\"source\":\"buffered\"}}";
        }

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            _written.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisposeDuringWriteTransport : IJsonLineTransport
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<int?>? Exited
        {
            add { }
            remove { }
        }

        public async IAsyncEnumerable<string> ReadLinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            WriteEntered.TrySetResult();
            await _disposed.Task.WaitAsync(cancellationToken);
            throw new ObjectDisposedException(nameof(DisposeDuringWriteTransport));
        }

        public ValueTask DisposeAsync()
        {
            _disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingWriteTransport : IJsonLineTransport
    {
        private readonly CancellationTokenSource _disposed = new();

        public TaskCompletionSource WriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<int?>? Exited
        {
            add { }
            remove { }
        }

        public async IAsyncEnumerable<string> ReadLinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposed.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            yield break;
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            WriteEntered.TrySetResult();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposed.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        }

        public ValueTask DisposeAsync()
        {
            _disposed.Cancel();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantNotificationTransport : IJsonLineTransport
    {
        private readonly TaskCompletionSource<long> _outerId =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<long> _nestedId =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _disposed = new();
        private int _writes;

        public event EventHandler<int?>? Exited
        {
            add { }
            remove { }
        }

        public async IAsyncEnumerable<string> ReadLinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposed.Token);
            var outerId = await _outerId.Task.WaitAsync(linked.Token);
            yield return "{\"method\":\"fake/reenter\"}";
            var nestedId = await _nestedId.Task.WaitAsync(linked.Token);
            yield return $"{{\"id\":{nestedId},\"result\":{{\"source\":\"nested\"}}}}";
            yield return $"{{\"id\":{outerId},\"result\":{{\"source\":\"outer\"}}}}";
        }

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            var id = document.RootElement.GetProperty("id").GetInt64();
            if (Interlocked.Increment(ref _writes) == 1)
            {
                _outerId.TrySetResult(id);
            }
            else
            {
                _nestedId.TrySetResult(id);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _disposed.Cancel();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDisposeTransport : IJsonLineTransport
    {
        public TaskCompletionSource WriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReaderExited { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<int?>? Exited
        {
            add { }
            remove { }
        }

        public async IAsyncEnumerable<string> ReadLinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                ReaderExited.TrySetResult();
            }

            yield break;
        }

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            WriteEntered.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await ReleaseDispose.Task;
        }
    }
}
