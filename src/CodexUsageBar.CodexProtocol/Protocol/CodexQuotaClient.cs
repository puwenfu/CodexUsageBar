using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CodexUsageBar.CodexProtocol.Transport;
using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Services;

namespace CodexUsageBar.CodexProtocol.Protocol;

public sealed class CodexQuotaClient : ICodexQuotaClient
{
    private const string CleanupFailureDataKey = "CodexUsageBar.CleanupFailed";
    private const long MinimumUnixSeconds = -62_135_596_800;
    private const long MaximumUnixSeconds = 253_402_300_799;

    [ThreadStatic]
    private static CodexQuotaClient? _dispatchingAccountChangedFor;

    [ThreadStatic]
    private static CodexQuotaClient? _dispatchingRefreshRequestedFor;

    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly ICodexAppServerSessionFactory _sessionFactory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly List<SessionRegistration> _cleanupPending = [];
    private readonly object _disposeSync = new();
    private readonly Channel<bool> _refreshRequests =
        Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly Task _refreshRequestDispatcherTask;
    private SessionRegistration? _session;
    private AccountFingerprint? _accountFingerprint;
    private TaskCompletionSource? _accountChangedDispatch;
    private Task? _disposeTask;
    private int _disposeRequested;

    public CodexQuotaClient()
        : this(new CodexAppServerSessionFactory(
            AppServerCommand.InstalledCodex,
            DefaultRequestTimeout))
    {
    }

    internal CodexQuotaClient(ICodexAppServerSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _refreshRequestDispatcherTask = DispatchRefreshRequestsAsync();
    }

    public event EventHandler<CodexAccountChangedEventArgs>? AccountChanged;

    public event EventHandler? RefreshRequested;

    public Task<QuotaSnapshot> RefreshAsync(CancellationToken cancellationToken) =>
        RefreshAsync(refreshOperationId: 0, cancellationToken);

    public async Task<QuotaSnapshot> RefreshAsync(
        long refreshOperationId,
        CancellationToken cancellationToken)
    {
        ThrowIfAccountChangedCallback();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
        await EnterLockAsync(cancellationToken);
        var operation = new RefreshOperation(refreshOperationId);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            return await RefreshCoreAsync(operation, cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
            operation.InstalledSession?.Activate(QueueRefreshRequested);
        }
    }

    public ValueTask DisposeAsync()
    {
        ThrowIfAccountChangedCallback();
        if (ReferenceEquals(_dispatchingRefreshRequestedFor, this))
        {
            return ValueTask.FromException(new InvalidOperationException(
                "The quota client cannot be disposed synchronously from its refresh-request handler."));
        }

        lock (_disposeSync)
        {
            Volatile.Write(ref _disposeRequested, 1);
            if (_disposeTask is null || _disposeTask.IsFaulted || _disposeTask.IsCanceled)
            {
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception? cleanupException = null;
        await EnterLockAsync(CancellationToken.None);
        try
        {
            if (_session is not null && !_cleanupPending.Contains(_session))
            {
                _cleanupPending.Add(_session);
            }

            _session = null;
            _accountFingerprint = null;
            foreach (var registeredSession in _cleanupPending.ToArray())
            {
                if (await TryDisposeSessionAsync(registeredSession, retainOnFailure: false))
                {
                    cleanupException ??= CreateCleanupException();
                }
                else
                {
                    _cleanupPending.Remove(registeredSession);
                }
            }

            if (cleanupException is not null)
            {
                throw cleanupException;
            }
        }
        finally
        {
            _refreshLock.Release();
            _refreshRequests.Writer.TryComplete();
            await _refreshRequestDispatcherTask;
        }
    }

    private async Task<QuotaSnapshot> RefreshCoreAsync(
        RefreshOperation operation,
        CancellationToken cancellationToken)
    {
        SessionRegistration? candidate = null;
        var accountChanged = false;
        try
        {
            candidate = new SessionRegistration(
                await _sessionFactory.StartAsync(cancellationToken));
            var account = await candidate.Session.ReadAccountAsync(cancellationToken)
                ?? throw new CodexSignedOutException();
            if (string.IsNullOrWhiteSpace(account.Type))
            {
                throw new CodexProtocolCompatibilityException();
            }

            var fingerprint = new AccountFingerprint(
                account.Type,
                account.Email ?? string.Empty,
                account.PlanType ?? string.Empty);
            accountChanged = _accountFingerprint is not null
                && (IsUnstable(account) || _accountFingerprint != fingerprint);
            if (accountChanged)
            {
                RaiseAccountChangedOutsideLock(operation.RefreshOperationId);
            }

            var rateLimits = await candidate.Session.ReadRateLimitsAsync(cancellationToken);
            var snapshot = CreateSnapshot(rateLimits);

            var oldSession = _session;
            _session = candidate;
            _accountFingerprint = fingerprint;
            operation.InstalledSession = candidate;
            candidate = null;
            try
            {
                await DisposeSessionAsync(oldSession);
            }
            catch
            {
                if (oldSession is not null && !_cleanupPending.Contains(oldSession))
                {
                    _cleanupPending.Add(oldSession);
                }

                throw CreateCleanupException();
            }

            return snapshot;
        }
        catch (Exception refreshException)
        {
            if (operation.InstalledSession is not null)
            {
                ExceptionDispatchInfo.Capture(refreshException).Throw();
            }

            var cleanupFailed = await TryDisposeSessionAsync(candidate, retainOnFailure: true);
            if (accountChanged)
            {
                var oldSession = _session;
                _session = null;
                _accountFingerprint = null;
                cleanupFailed |= await TryDisposeSessionAsync(oldSession, retainOnFailure: true);
            }

            if (cleanupFailed)
            {
                refreshException.Data[CleanupFailureDataKey] = true;
            }

            ExceptionDispatchInfo.Capture(refreshException).Throw();
            throw;
        }
    }

    private static QuotaSnapshot CreateSnapshot(CodexRateLimitsReadResult response)
    {
        var bucket = SelectBucket(response);
        var measurements = new List<QuotaMeasurement>(2);
        AddMeasurement(measurements, bucket?.Primary);
        AddMeasurement(measurements, bucket?.Secondary);

        try
        {
            return QuotaSnapshotFactory.Create(measurements, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is
            QuotaCompatibilityException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new CodexProtocolCompatibilityException();
        }
    }

    private static CodexRateLimitBucket? SelectBucket(CodexRateLimitsReadResult response)
    {
        if (response.RateLimitsByLimitId is not null
            && response.RateLimitsByLimitId.TryGetValue("codex", out var codexBucket)
            && codexBucket is not null)
        {
            return codexBucket;
        }

        return response.RateLimits;
    }

    private static void AddMeasurement(
        ICollection<QuotaMeasurement> measurements,
        CodexRateLimitWindow? window)
    {
        if (window is null)
        {
            return;
        }

        if (window.UsedPercent is not double usedPercent
            || double.IsNaN(usedPercent)
            || double.IsInfinity(usedPercent)
            || usedPercent < int.MinValue
            || usedPercent > int.MaxValue
            || window.ResetsAt is < MinimumUnixSeconds or > MaximumUnixSeconds)
        {
            throw new CodexProtocolCompatibilityException();
        }

        measurements.Add(new QuotaMeasurement(
            window.WindowDurationMins,
            checked((int)Math.Round(usedPercent, MidpointRounding.AwayFromZero)),
            window.ResetsAt));
    }

    private static bool IsUnstable(CodexAccount account) =>
        account.Email is null
        && string.Equals(account.Type, "chatgpt", StringComparison.OrdinalIgnoreCase);

    private async Task EnterLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _refreshLock.WaitAsync(cancellationToken);
            var dispatch = _accountChangedDispatch;
            if (dispatch is null)
            {
                return;
            }

            _refreshLock.Release();
            await dispatch.Task.WaitAsync(cancellationToken);
        }
    }

    private void RaiseAccountChangedOutsideLock(long refreshOperationId)
    {
        var dispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _accountChangedDispatch = dispatch;
        _refreshLock.Release();
        try
        {
            var previousDispatch = _dispatchingAccountChangedFor;
            _dispatchingAccountChangedFor = this;
            try
            {
                var eventArgs = new CodexAccountChangedEventArgs(refreshOperationId);
                foreach (EventHandler<CodexAccountChangedEventArgs> handler
                    in AccountChanged?.GetInvocationList() ?? [])
                {
                    try
                    {
                        handler(this, eventArgs);
                    }
                    catch
                    {
                        // A subscriber cannot stop account verification or other subscribers.
                    }
                }
            }
            finally
            {
                _dispatchingAccountChangedFor = previousDispatch;
            }
        }
        finally
        {
            dispatch.TrySetResult();
            _refreshLock.Wait();
            _accountChangedDispatch = null;
        }
    }

    private void QueueRefreshRequested()
    {
        if (Volatile.Read(ref _disposeRequested) == 0)
        {
            _refreshRequests.Writer.TryWrite(true);
        }
    }

    private async Task DispatchRefreshRequestsAsync()
    {
        await foreach (var _ in _refreshRequests.Reader.ReadAllAsync())
        {
            foreach (EventHandler handler in RefreshRequested?.GetInvocationList() ?? [])
            {
                var previousDispatch = _dispatchingRefreshRequestedFor;
                try
                {
                    _dispatchingRefreshRequestedFor = this;
                    handler(this, EventArgs.Empty);
                }
                catch
                {
                    // A subscriber cannot stop notification dispatch or another subscriber.
                }
                finally
                {
                    _dispatchingRefreshRequestedFor = previousDispatch;
                }
            }
        }
    }

    private void ThrowIfAccountChangedCallback()
    {
        if (ReferenceEquals(_dispatchingAccountChangedFor, this))
        {
            throw new InvalidOperationException(
                "The quota client cannot refresh or dispose synchronously from its account-change handler.");
        }
    }

    private static async Task DisposeSessionAsync(SessionRegistration? session)
    {
        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    private async Task<bool> TryDisposeSessionAsync(
        SessionRegistration? session,
        bool retainOnFailure)
    {
        try
        {
            await DisposeSessionAsync(session);
            return false;
        }
        catch
        {
            if (retainOnFailure
                && session is not null
                && !_cleanupPending.Contains(session))
            {
                _cleanupPending.Add(session);
            }

            return true;
        }
    }

    private static CodexJsonRpcException CreateCleanupException() =>
        new("The Codex app-server session cleanup failed.");

    private sealed class SessionRegistration : IAsyncDisposable
    {
        private readonly object _sync = new();
        private Action? _refreshRequested;
        private bool _pendingRefresh;
        private bool _disposed;

        public SessionRegistration(ICodexAppServerSession session)
        {
            Session = session;
            Session.NotificationReceived += OnNotificationReceived;
        }

        public ICodexAppServerSession Session { get; }

        public void Activate(Action refreshRequested)
        {
            var raiseRefresh = false;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _refreshRequested = refreshRequested;
                raiseRefresh = _pendingRefresh;
                _pendingRefresh = false;
            }

            if (raiseRefresh)
            {
                refreshRequested();
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _refreshRequested = null;
                _pendingRefresh = false;
            }

            Session.NotificationReceived -= OnNotificationReceived;
            try
            {
                await Session.DisposeAsync();
            }
            catch
            {
                lock (_sync)
                {
                    _disposed = false;
                }

                throw;
            }
        }

        private void OnNotificationReceived(
            object? sender,
            CodexAppServerNotificationEventArgs eventArgs)
        {
            if (eventArgs.Method is not ("account/updated" or "account/rateLimits/updated"))
            {
                return;
            }

            Action? refreshRequested;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                refreshRequested = _refreshRequested;
                if (refreshRequested is null)
                {
                    _pendingRefresh = true;
                    return;
                }
            }

            refreshRequested();
        }
    }

    private sealed record AccountFingerprint(
        string AccountType,
        string Email,
        string PlanType);

    private sealed class RefreshOperation(long refreshOperationId)
    {
        public long RefreshOperationId { get; } = refreshOperationId;

        public SessionRegistration? InstalledSession { get; set; }
    }
}
