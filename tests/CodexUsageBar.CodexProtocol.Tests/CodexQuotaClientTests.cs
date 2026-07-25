using System.Diagnostics;
using CodexUsageBar.CodexProtocol.Protocol;
using CodexUsageBar.CodexProtocol.Transport;
using CodexUsageBar.FakeAppServer;

namespace CodexUsageBar.CodexProtocol.Tests;

public sealed class CodexQuotaClientTests
{
    [Fact]
    public async Task RefreshAsync_PrefersCodexBucket()
    {
        await using var client = CreateClient("codex-bucket");

        var snapshot = await client.RefreshAsync(CancellationToken.None);

        Assert.Equal(72, snapshot.FiveHour!.RemainingPercent);
        Assert.Equal(41, snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public async Task RefreshAsync_FallsBackToLegacyRateLimits()
    {
        await using var client = CreateClient("legacy-bucket");

        var snapshot = await client.RefreshAsync(CancellationToken.None);

        Assert.Equal(65, snapshot.FiveHour!.RemainingPercent);
        Assert.Equal(54, snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public async Task RefreshAsync_RejectsUnknownWindowDuration()
    {
        await using var client = CreateClient("unknown-window");

        var exception = await Assert.ThrowsAsync<CodexProtocolCompatibilityException>(
            () => client.RefreshAsync(CancellationToken.None));

        Assert.DoesNotContain("windowDurationMins", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_RaisesAccountChangedBeforeReadingNewQuota()
    {
        var factory = new RecordingSessionFactory("account-a", "account-b", "account-a");
        await using var client = new CodexQuotaClient(factory);
        var changes = 0;
        client.AccountChanged += (_, _) =>
        {
            Assert.Equal(changes + 1, factory.RateLimitReadCount);
            changes++;
        };

        await client.RefreshAsync(CancellationToken.None);
        await client.RefreshAsync(CancellationToken.None);
        await client.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, changes);
        Assert.Equal(3, factory.StartCount);
        Assert.Equal(3, factory.RateLimitReadCount);
        Assert.Equal(1, factory.LiveSessionCount);
    }

    [Fact]
    public async Task RefreshAsync_AccountChangedCarriesCallingOperationId()
    {
        var factory = new RecordingSessionFactory("account-a", "account-b");
        await using var client = new CodexQuotaClient(factory);
        long? observedOperationId = null;
        client.AccountChanged += (_, eventArgs) =>
            observedOperationId = eventArgs.RefreshOperationId;

        await client.RefreshAsync(refreshOperationId: 101, CancellationToken.None);
        await client.RefreshAsync(refreshOperationId: 202, CancellationToken.None);

        Assert.Equal(202, observedOperationId);
    }

    [Fact]
    public async Task RefreshAsync_TreatsChatGptNullEmailAsUnstableAfterFirstSuccess()
    {
        var factory = new RecordingSessionFactory(
            "chatgpt-null-email",
            "chatgpt-null-email",
            "chatgpt-null-email");
        await using var client = new CodexQuotaClient(factory);
        var changes = 0;
        client.AccountChanged += (_, _) => changes++;

        await client.RefreshAsync(CancellationToken.None);
        await client.RefreshAsync(CancellationToken.None);
        await client.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, changes);
        Assert.Equal(1, factory.LiveSessionCount);
    }

    [Fact]
    public async Task RefreshAsync_ThrowsSignedOutAndCleansFailedSession()
    {
        var factory = new RecordingSessionFactory("signed-out");
        await using var client = new CodexQuotaClient(factory);

        var exception = await Assert.ThrowsAsync<CodexSignedOutException>(
            () => client.RefreshAsync(CancellationToken.None));

        Assert.Equal("Codex is signed out.", exception.Message);
        Assert.Equal(0, factory.LiveSessionCount);
    }

    [Theory]
    [InlineData("account-notification")]
    [InlineData("rate-notification")]
    public async Task SessionNotification_RaisesParameterlessRefreshRequested(string scenario)
    {
        await using var client = CreateClient(scenario);
        var refreshed = new TaskCompletionSource<EventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RefreshRequested += (_, eventArgs) => refreshed.TrySetResult(eventArgs);

        await client.RefreshAsync(CancellationToken.None);

        var eventArgs = await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(EventArgs.Empty, eventArgs);
    }

    [Fact]
    public async Task SessionNotification_BeforeRateLimitResponseIsNotLost()
    {
        await using var client = CreateClient("early-rate-notification");
        var refreshed = new TaskCompletionSource<EventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RefreshRequested += (_, eventArgs) => refreshed.TrySetResult(eventArgs);

        await client.RefreshAsync(CancellationToken.None);

        Assert.Same(
            EventArgs.Empty,
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task RefreshRequested_SynchronousRefreshCompletesAndLeavesOneLiveSession()
    {
        var realFactory = new RecordingSessionFactory("rate-notification-auto-exit");
        var candidate = ScriptedSession.Success("account-b");
        var client = new CodexQuotaClient(new SequencedSessionFactory(
            realFactory,
            new ScriptedSessionFactory(candidate)));
        var callbackResult = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RefreshRequested += (_, _) =>
        {
            try
            {
                client.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                callbackResult.TrySetResult(null);
            }
            catch (Exception exception)
            {
                callbackResult.TrySetResult(exception);
            }
        };

        try
        {
            await client.RefreshAsync(CancellationToken.None);
            var exception = await callbackResult.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Null(exception);
            Assert.Equal(1, realFactory.StartCount);
            Assert.False(candidate.DisposeAttempted);
        }
        finally
        {
            await client.DisposeAsync();
        }

        Assert.True(candidate.DisposeAttempted);
    }

    [Fact]
    public async Task RefreshRequested_SynchronousDisposeFailsFastWithoutDeadlock()
    {
        var factory = new RecordingSessionFactory("rate-notification-auto-exit");
        var client = new CodexQuotaClient(factory);
        var callbackResult = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RefreshRequested += (_, _) =>
        {
            try
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
                callbackResult.TrySetResult(null);
            }
            catch (Exception exception)
            {
                callbackResult.TrySetResult(exception);
            }
        };

        await client.RefreshAsync(CancellationToken.None);
        var exception = await callbackResult.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.IsType<InvalidOperationException>(exception);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task RefreshRequested_HandlerFailureDoesNotStopLaterHandler()
    {
        await using var client = CreateClient("rate-notification");
        var laterHandler = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RefreshRequested += (_, _) => throw new InvalidOperationException("handler failure");
        client.RefreshRequested += (_, _) => laterHandler.TrySetResult();

        await client.RefreshAsync(CancellationToken.None);

        await laterHandler.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RefreshAsync_RequestTimeoutCleansFailedSession()
    {
        var factory = new RecordingSessionFactory(TimeSpan.FromMilliseconds(100), "request-timeout");
        await using var client = new CodexQuotaClient(factory);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.RefreshAsync(CancellationToken.None));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal(0, factory.LiveSessionCount);
    }

    [Fact]
    public async Task RefreshAsync_FailureAfterAccountChangeDisposesOldAndFailedSessions()
    {
        var factory = new RecordingSessionFactory(
            TimeSpan.FromMilliseconds(100),
            "account-a",
            "account-b-rate-timeout");
        await using var client = new CodexQuotaClient(factory);
        var changes = 0;
        client.AccountChanged += (_, _) => changes++;
        await client.RefreshAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.RefreshAsync(CancellationToken.None));

        Assert.Equal(1, changes);
        Assert.Equal(0, factory.LiveSessionCount);
    }

    [Fact]
    public async Task RefreshAsync_CandidateDisposeFailureStillAttemptsOldSessionCleanup()
    {
        var first = ScriptedSession.Success("account-a");
        var second = ScriptedSession.RateFailure("account-b", disposeThrows: true);
        var factory = new ScriptedSessionFactory(first, second);
        var client = new CodexQuotaClient(factory);
        try
        {
            await client.RefreshAsync(CancellationToken.None);
            first.DisposeThrows = true;

            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => client.RefreshAsync(CancellationToken.None));

            Assert.Equal("sanitized scripted timeout", exception.Message);
            Assert.True(second.DisposeAttempted);
            Assert.True(first.DisposeAttempted);
        }
        finally
        {
            first.DisposeThrows = false;
            second.DisposeThrows = false;
            await client.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("account-a")]
    [InlineData("account-b")]
    public async Task RefreshAsync_OldDisposeFailureStillActivatesInstalledSession(
        string nextIdentity)
    {
        var first = ScriptedSession.Success("account-a");
        var second = ScriptedSession.Success(nextIdentity);
        second.NotifyDuringRateRead = true;
        var factory = new ScriptedSessionFactory(first, second);
        var client = new CodexQuotaClient(factory);
        try
        {
            await client.RefreshAsync(CancellationToken.None);
            first.DisposeThrows = true;
            var refreshed = new TaskCompletionSource<EventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            client.RefreshRequested += (_, eventArgs) => refreshed.TrySetResult(eventArgs);

            var exception = await Assert.ThrowsAsync<CodexJsonRpcException>(
                () => client.RefreshAsync(CancellationToken.None));

            Assert.DoesNotContain("scripted", exception.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.True(first.DisposeAttempted);
            Assert.False(second.DisposeAttempted);
            Assert.Same(
                EventArgs.Empty,
                await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            first.DisposeThrows = false;
            await client.DisposeAsync();
        }

        Assert.Equal(2, first.DisposeAttemptCount);
        Assert.True(second.DisposeAttempted);
    }

    [Fact]
    public async Task AccountChanged_SynchronousRefreshFailsFastWithoutBlockingOuterRefresh()
    {
        var factory = new RecordingSessionFactory("account-a", "account-b");
        await using var client = new CodexQuotaClient(factory);
        await client.RefreshAsync(CancellationToken.None);
        Exception? nestedException = null;
        client.AccountChanged += (_, _) =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            nestedException = Record.Exception(() =>
                client.RefreshAsync(cancellation.Token).GetAwaiter().GetResult());
        };

        var snapshot = await client.RefreshAsync(CancellationToken.None);

        Assert.IsType<InvalidOperationException>(nestedException);
        Assert.NotNull(snapshot.FiveHour);
    }

    [Fact]
    public async Task AccountChanged_HandlerFailureDoesNotStopRefreshOrOtherHandlers()
    {
        var factory = new RecordingSessionFactory("account-a", "account-b");
        await using var client = new CodexQuotaClient(factory);
        await client.RefreshAsync(CancellationToken.None);
        var laterHandlerCalled = false;
        client.AccountChanged += (_, _) => throw new InvalidOperationException("handler failure");
        client.AccountChanged += (_, _) => laterHandlerCalled = true;

        var snapshot = await client.RefreshAsync(CancellationToken.None);

        Assert.True(laterHandlerCalled);
        Assert.NotNull(snapshot.FiveHour);
        Assert.Equal(1, factory.LiveSessionCount);
    }

    [Theory]
    [InlineData("oversized-used-percent")]
    [InlineData("out-of-range-reset")]
    [InlineData("missing-account-type")]
    [InlineData("null-account-type")]
    [InlineData("empty-account-type")]
    public async Task RefreshAsync_TranslatesMalformedProtocolValues(string scenario)
    {
        await using var client = CreateClient(scenario);

        await Assert.ThrowsAsync<CodexProtocolCompatibilityException>(
            () => client.RefreshAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_CleansInstalledSession()
    {
        var factory = new RecordingSessionFactory("account-a");
        var client = new CodexQuotaClient(factory);
        await client.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, factory.LiveSessionCount);

        await client.DisposeAsync();

        Assert.Equal(0, factory.LiveSessionCount);
    }

    [Fact]
    public async Task DisposeAsync_RetriesRetainedSessionAfterCleanupFailure()
    {
        var session = ScriptedSession.Success("account-a");
        session.DisposeThrows = true;
        var client = new CodexQuotaClient(new ScriptedSessionFactory(session));
        await client.RefreshAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CodexJsonRpcException>(
            () => client.DisposeAsync().AsTask());
        session.DisposeThrows = false;

        await client.DisposeAsync();

        Assert.DoesNotContain("scripted", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, session.DisposeAttemptCount);
        Assert.Equal(0, session.LiveResourceCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallersAwaitSameCleanupAttempt()
    {
        var session = ScriptedSession.Success("account-a");
        session.DisposeRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new CodexQuotaClient(new ScriptedSessionFactory(session));
        await client.RefreshAsync(CancellationToken.None);
        var firstDispose = client.DisposeAsync().AsTask();
        await session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var secondDispose = client.DisposeAsync().AsTask();

        try
        {
            Assert.Same(firstDispose, secondDispose);
            Assert.False(secondDispose.IsCompleted);
        }
        finally
        {
            session.DisposeRelease.TrySetResult();
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(2));
            await secondDispose.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(1, session.DisposeAttemptCount);
        Assert.Equal(0, session.LiveResourceCount);
    }

    private static CodexQuotaClient CreateClient(string scenario) =>
        new(new CodexAppServerSessionFactory(FakeCommand(scenario), TimeSpan.FromSeconds(2)));

    private static AppServerCommand FakeCommand(string scenario) =>
        new("dotnet", [typeof(FakeAppServerMarker).Assembly.Location, scenario]);

    private sealed class RecordingSessionFactory : ICodexAppServerSessionFactory
    {
        private readonly Queue<string> _scenarios;
        private readonly TimeSpan _requestTimeout;
        private readonly object _sync = new();
        private int _liveSessionCount;

        public RecordingSessionFactory(params string[] scenarios)
            : this(TimeSpan.FromSeconds(2), scenarios)
        {
        }

        public RecordingSessionFactory(TimeSpan requestTimeout, params string[] scenarios)
        {
            _requestTimeout = requestTimeout;
            _scenarios = new Queue<string>(scenarios);
        }

        public int StartCount { get; private set; }

        public int RateLimitReadCount { get; private set; }

        public int LiveSessionCount => Volatile.Read(ref _liveSessionCount);

        public async Task<ICodexAppServerSession> StartAsync(CancellationToken cancellationToken)
        {
            string scenario;
            lock (_sync)
            {
                scenario = _scenarios.Dequeue();
                StartCount++;
            }

            var factory = new CodexAppServerSessionFactory(
                FakeCommand(scenario),
                _requestTimeout);
            var session = await factory.StartAsync(cancellationToken);
            Interlocked.Increment(ref _liveSessionCount);
            return new RecordingSession(
                session,
                () => RateLimitReadCount++,
                () => Interlocked.Decrement(ref _liveSessionCount));
        }
    }

    private sealed class RecordingSession(
        ICodexAppServerSession inner,
        Action rateLimitRead,
        Action disposed) : ICodexAppServerSession
    {
        private int _disposed;

        public event EventHandler<CodexAppServerNotificationEventArgs>? NotificationReceived
        {
            add => inner.NotificationReceived += value;
            remove => inner.NotificationReceived -= value;
        }

        public Task<CodexAccount?> ReadAccountAsync(CancellationToken cancellationToken) =>
            inner.ReadAccountAsync(cancellationToken);

        public Task<CodexRateLimitsReadResult> ReadRateLimitsAsync(
            CancellationToken cancellationToken)
        {
            rateLimitRead();
            return inner.ReadRateLimitsAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await inner.DisposeAsync();
            }
            finally
            {
                disposed();
            }
        }
    }

    private sealed class ScriptedSessionFactory(params ScriptedSession[] sessions)
        : ICodexAppServerSessionFactory
    {
        private readonly Queue<ScriptedSession> _sessions = new(sessions);

        public Task<ICodexAppServerSession> StartAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ICodexAppServerSession>(_sessions.Dequeue());
    }

    private sealed class SequencedSessionFactory(
        params ICodexAppServerSessionFactory[] factories) : ICodexAppServerSessionFactory
    {
        private readonly Queue<ICodexAppServerSessionFactory> _factories = new(factories);

        public Task<ICodexAppServerSession> StartAsync(CancellationToken cancellationToken) =>
            _factories.Dequeue().StartAsync(cancellationToken);
    }

    private sealed class ScriptedSession(
        CodexAccount account,
        Exception? rateFailure,
        bool disposeThrows) : ICodexAppServerSession
    {
        public bool DisposeAttempted { get; private set; }

        public int DisposeAttemptCount { get; private set; }

        public int LiveResourceCount { get; private set; } = 1;

        public bool DisposeThrows { get; set; } = disposeThrows;

        public bool NotifyDuringRateRead { get; set; }

        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? DisposeRelease { get; set; }

        public event EventHandler<CodexAppServerNotificationEventArgs>? NotificationReceived;

        public static ScriptedSession Success(string identity) =>
            new(
                new CodexAccount("chatgpt", identity, "pro"),
                null,
                disposeThrows: false);

        public static ScriptedSession RateFailure(string identity, bool disposeThrows) =>
            new(
                new CodexAccount("chatgpt", identity, "pro"),
                new TimeoutException("sanitized scripted timeout"),
                disposeThrows);

        public Task<CodexAccount?> ReadAccountAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CodexAccount?>(account);

        public Task<CodexRateLimitsReadResult> ReadRateLimitsAsync(
            CancellationToken cancellationToken)
        {
            if (NotifyDuringRateRead)
            {
                NotificationReceived?.Invoke(
                    this,
                    new CodexAppServerNotificationEventArgs("account/rateLimits/updated"));
            }

            if (rateFailure is not null)
            {
                return Task.FromException<CodexRateLimitsReadResult>(rateFailure);
            }

            var bucket = new CodexRateLimitBucket(
                new CodexRateLimitWindow(10, null, 300),
                new CodexRateLimitWindow(20, null, 10_080));
            return Task.FromResult(new CodexRateLimitsReadResult(bucket, null));
        }

        public async ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            DisposeAttemptCount++;
            DisposeEntered.TrySetResult();
            if (DisposeRelease is not null)
            {
                await DisposeRelease.Task;
            }

            if (DisposeThrows)
            {
                throw new InvalidOperationException("sanitized dispose failure");
            }

            LiveResourceCount = 0;
        }
    }
}
