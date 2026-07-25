using System.IO;
using CodexUsageBar.App.Diagnostics;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.CodexProtocol.Protocol;
using CodexUsageBar.CodexProtocol.Transport;
using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.App.Services;

public enum RefreshReason
{
    Startup,
    Automatic,
    Manual,
    AccountChanged,
    ProtocolNotification,
}

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public interface IRefreshRequester
{
    Task RequestRefresh(RefreshReason reason);
}

public sealed class RefreshCoordinator : IRefreshRequester, IAsyncDisposable
{
    private static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(60);
    private static readonly WidgetDisplayModel AccountSwitchDisplay = new(
        new QuotaDisplayWindow("--", "5h", "--", 1),
        new QuotaDisplayWindow("--", "周", "--", 1),
        "正在读取切换后的 Codex 账户额度。",
        IsRefreshing: true,
        IsStale: false);
    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ICodexQuotaClient _client;
    private readonly QuotaPresentationService _presentation;
    private readonly IClock _clock;
    private readonly IDiagnosticLogger _logger;
    private readonly IAsyncDelay _delay;
    private readonly DebugViewModel _debugViewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private WidgetViewModel? _viewModel;
    private WidgetDisplayModel? _lastBaseDisplay;
    private SynchronizationContext? _viewContext;
    private int _viewThreadId;
    private RefreshAttempt? _activeAttempt;
    private QuotaSnapshot? _snapshot;
    private DateTimeOffset? _lastSuccessfulAt;
    private Task? _refreshRunner;
    private Task? _timerTask;
    private Task? _disposeTask;
    private Task _accountClearBarrier = Task.CompletedTask;
    private CancellationTokenSource? _scheduledDelayCancellation;
    private TimeSpan _nextDelay = NormalInterval;
    private long _accountGeneration;
    private bool _pending;
    private bool _started;
    private bool _disposed;
    private int _consecutiveFailures;
    private long _nextRefreshOperationId;

    internal Action? BeforeRunnerFinalTransition { get; set; }
    internal Action? BeforeReadyPublish { get; set; }

    public RefreshCoordinator(
        ICodexQuotaClient client,
        QuotaPresentationService presentation,
        IClock clock,
        IDiagnosticLogger logger,
        DebugViewModel debugViewModel,
        IAsyncDelay? delay = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _debugViewModel = debugViewModel ?? throw new ArgumentNullException(nameof(debugViewModel));
        _delay = delay ?? new SystemAsyncDelay();
        _client.AccountChanged += OnAccountChanged;
        _client.RefreshRequested += OnRefreshRequested;
        _debugViewModel.DataChanged += OnDebugDataChanged;
    }

    private void OnDebugDataChanged(object? sender, EventArgs e)
    {
        WidgetDisplayModel? display;
        long accountGeneration;
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }

            display = _lastBaseDisplay;
            accountGeneration = _accountGeneration;
        }

        if (display is not null)
        {
            ApplyDisplay(display, accountGeneration);
        }
    }

    public TimeSpan NextDelay
    {
        get
        {
            lock (_stateSync)
            {
                return _nextDelay;
            }
        }
    }

    public int ConsecutiveFailures
    {
        get
        {
            lock (_stateSync)
            {
                return _consecutiveFailures;
            }
        }
    }

    public void Attach(WidgetViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        lock (_stateSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_viewModel is not null)
            {
                throw new InvalidOperationException("A refresh coordinator can attach only one view model.");
            }

            _viewModel = viewModel;
            _viewContext = SynchronizationContext.Current;
            _viewThreadId = Environment.CurrentManagedThreadId;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The refresh coordinator has already started.");
            }

            _started = true;
        }

        await RequestRefresh(RefreshReason.Startup).WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateSync)
        {
            if (!_disposed)
            {
                _timerTask = RunTimerAsync();
            }
        }
    }

    public Task RequestRefresh(RefreshReason reason)
    {
        _ = reason;
        TaskCompletionSource? completion = null;
        Task runner;
        lock (_stateSync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_refreshRunner is { IsCompleted: false })
            {
                _pending = true;
                return _refreshRunner;
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runner = completion.Task;
            _refreshRunner = runner;
        }

        _ = RunPublishedPumpAsync(completion);
        return runner;
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task RunPublishedPumpAsync(TaskCompletionSource completion)
    {
        while (true)
        {
            Exception? failure = null;
            try
            {
                await RunRefreshCycleAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            BeforeRunnerFinalTransition?.Invoke();
            lock (_stateSync)
            {
                if (failure is null && _pending && !_disposed)
                {
                    _pending = false;
                    continue;
                }

                if (_disposed)
                {
                    _pending = false;
                }

                if (failure is null)
                {
                    completion.TrySetResult();
                }
                else
                {
                    completion.TrySetException(failure);
                }

                if (ReferenceEquals(_refreshRunner, completion.Task))
                {
                    _refreshRunner = null;
                }

                return;
            }
        }
    }

    private async Task RunRefreshCycleAsync()
    {
        try
        {
            await _refreshGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                await RefreshOnceAsync(_lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        RefreshAttempt? attempt = null;
        try
        {
            var accountGeneration = await WaitForAccountClearAsync(cancellationToken).ConfigureAwait(false);
            attempt = new RefreshAttempt(
                Interlocked.Increment(ref _nextRefreshOperationId),
                accountGeneration);
            lock (_stateSync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _activeAttempt = attempt;
            }

            var snapshot = await _client
                .RefreshAsync(attempt.OperationId, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            BeforeReadyPublish?.Invoke();
            lock (_stateSync)
            {
                if (attempt.AccountGeneration != _accountGeneration)
                {
                    return;
                }

                _snapshot = snapshot;
                _lastSuccessfulAt = _clock.Now;
                _consecutiveFailures = 0;
                _nextDelay = NormalInterval;
                Publish(_presentation.Create(
                    snapshot,
                    WidgetStatus.Ready,
                    _lastSuccessfulAt,
                    recoveryHint: null),
                    attempt.AccountGeneration);
            }

            _logger.Write(new DiagnosticEvent("refresh.succeeded", "ready", 60, string.Empty, null));
            RescheduleTimer();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ApplyFailure(exception, attempt);
        }
        finally
        {
            lock (_stateSync)
            {
                if (ReferenceEquals(_activeAttempt, attempt))
                {
                    _activeAttempt = null;
                }
            }
        }
    }

    private async Task<long> WaitForAccountClearAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task barrier;
            lock (_stateSync)
            {
                barrier = _accountClearBarrier;
            }

            await barrier.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateSync)
            {
                if (!ReferenceEquals(barrier, _accountClearBarrier))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var accountGeneration = _accountGeneration;
                Publish(_presentation.Create(
                    _snapshot,
                    WidgetStatus.Refreshing,
                    _lastSuccessfulAt,
                    recoveryHint: null),
                    accountGeneration);
                return accountGeneration;
            }
        }
    }

    private void ApplyFailure(Exception exception, RefreshAttempt? expectedAttempt)
    {
        var failure = Classify(exception);
        TimeSpan nextDelay;
        int attempt;
        lock (_stateSync)
        {
            if (expectedAttempt is not null
                && expectedAttempt.AccountGeneration != _accountGeneration)
            {
                return;
            }

            attempt = ++_consecutiveFailures;
            _nextDelay = RetryPolicy.NextDelay(failure.Kind, attempt);
            nextDelay = _nextDelay;
            if (failure.ClearSnapshot)
            {
                _snapshot = null;
                _lastSuccessfulAt = null;
            }

            Publish(_presentation.Create(
                _snapshot,
                failure.Status,
                _lastSuccessfulAt,
                failure.Hint),
                expectedAttempt?.AccountGeneration ?? _accountGeneration);
        }

        _logger.Write(
            new DiagnosticEvent(
                "refresh.failed",
                failure.Kind.ToString().ToLowerInvariant(),
                checked((int)nextDelay.TotalSeconds),
                string.Empty,
                null),
            exception);
        RescheduleTimer();
    }

    private async Task RunTimerAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            TimeSpan delay;
            CancellationTokenSource scheduledDelay;
            lock (_stateSync)
            {
                if (_disposed)
                {
                    return;
                }

                delay = _nextDelay;
                scheduledDelay = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _scheduledDelayCancellation = scheduledDelay;
            }

            var elapsed = false;
            try
            {
                await _delay.DelayAsync(delay, scheduledDelay.Token).ConfigureAwait(false);
                elapsed = true;
            }
            catch (OperationCanceledException) when (scheduledDelay.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_stateSync)
                {
                    if (ReferenceEquals(_scheduledDelayCancellation, scheduledDelay))
                    {
                        _scheduledDelayCancellation = null;
                    }
                }

                scheduledDelay.Dispose();
            }

            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (elapsed)
            {
                await RequestRefresh(RefreshReason.Automatic).ConfigureAwait(false);
            }
        }
    }

    private void RescheduleTimer()
    {
        CancellationTokenSource? scheduledDelay;
        lock (_stateSync)
        {
            scheduledDelay = _scheduledDelayCancellation;
        }

        try
        {
            scheduledDelay?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The timer completed between capture and cancellation.
        }
    }

    private void OnAccountChanged(object? sender, CodexAccountChangedEventArgs eventArgs)
    {
        var clearCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RefreshAttempt? ownedAttempt;
        long accountGeneration;
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }

            _snapshot = null;
            _lastSuccessfulAt = null;
            accountGeneration = ++_accountGeneration;
            _accountClearBarrier = clearCompletion.Task;
            _lastBaseDisplay = AccountSwitchDisplay;
            ownedAttempt = _activeAttempt is { } activeAttempt
                && eventArgs.RefreshOperationId == activeAttempt.OperationId
                ? activeAttempt
                : null;
        }

        ApplyDisplay(AccountSwitchDisplay, accountGeneration, clearCompletion);
        WaitForAccountClearOrShutdown(clearCompletion.Task);
        if (ownedAttempt is not null)
        {
            lock (_stateSync)
            {
                if (ReferenceEquals(_activeAttempt, ownedAttempt) && !_disposed)
                {
                    ownedAttempt.AccountGeneration = accountGeneration;
                }
            }

            return;
        }

        _ = RequestRefresh(RefreshReason.AccountChanged);
    }

    private void WaitForAccountClearOrShutdown(Task clearCompletion)
    {
        try
        {
            clearCompletion.WaitAsync(_lifetime.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void OnRefreshRequested(object? sender, EventArgs eventArgs) =>
        _ = RequestRefresh(RefreshReason.ProtocolNotification);

    private void Publish(WidgetDisplayModel display, long expectedAccountGeneration)
    {
        lock (_stateSync)
        {
            if (_disposed || expectedAccountGeneration != _accountGeneration)
            {
                return;
            }

            _lastBaseDisplay = display;
        }

        ApplyDisplay(display, expectedAccountGeneration);
    }

    private void ApplyDisplay(
        WidgetDisplayModel display,
        long expectedAccountGeneration,
        TaskCompletionSource? completion = null) =>
        DispatchToView(viewModel =>
        {
            lock (_stateSync)
            {
                if (_disposed || expectedAccountGeneration != _accountGeneration)
                {
                    return;
                }

                viewModel.Apply(_debugViewModel.OverrideDisplay(display));
            }
        }, completion);

    private void DispatchToView(
        Action<WidgetViewModel> action,
        TaskCompletionSource? completion = null)
    {
        WidgetViewModel? viewModel;
        SynchronizationContext? context;
        int viewThreadId;
        lock (_stateSync)
        {
            viewModel = _viewModel;
            context = _viewContext;
            viewThreadId = _viewThreadId;
        }

        if (viewModel is null)
        {
            completion?.TrySetResult();
            return;
        }

        if (context is null
            || Environment.CurrentManagedThreadId == viewThreadId
            || ReferenceEquals(context, SynchronizationContext.Current))
        {
            try
            {
                action(viewModel);
            }
            finally
            {
                completion?.TrySetResult();
            }

            return;
        }

        try
        {
            context.Post(static state =>
            {
                var dispatch = (ViewDispatch)state!;
                try
                {
                    dispatch.Action(dispatch.ViewModel);
                }
                finally
                {
                    dispatch.Completion?.TrySetResult();
                }
            }, new ViewDispatch(viewModel, action, completion));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or ObjectDisposedException
                or NotSupportedException)
        {
            completion?.TrySetResult();
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task? refreshRunner;
        Task? timerTask;
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = false;
            _client.AccountChanged -= OnAccountChanged;
            _client.RefreshRequested -= OnRefreshRequested;
            _debugViewModel.DataChanged -= OnDebugDataChanged;
            _lifetime.Cancel();
            refreshRunner = _refreshRunner;
            timerTask = _timerTask;
        }

        await IgnoreCancellationAsync(refreshRunner).ConfigureAwait(false);
        await IgnoreCancellationAsync(timerTask).ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _refreshGate.Dispose();
    }

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Client disposal must run even if a UI callback or pump completion faulted.
        }
    }

    private static FailureClassification Classify(Exception exception) => exception switch
    {
        CodexCommandNotFoundException => new(
            FailureKind.CommandMissing,
            WidgetStatus.MissingCodex,
            "未找到 Codex，请先安装或更新 Codex。",
            ClearSnapshot: true),
        CodexSignedOutException => new(
            FailureKind.SignedOut,
            WidgetStatus.SignedOut,
            "Codex 尚未登录，请先在 Codex 中登录。",
            ClearSnapshot: true),
        CodexProtocolCompatibilityException => new(
            FailureKind.Incompatible,
            WidgetStatus.Incompatible,
            "额度接口暂不兼容，请更新本工具。",
            ClearSnapshot: false),
        TimeoutException => new(
            FailureKind.Timeout,
            WidgetStatus.Offline,
            "额度读取超时，正在重试。",
            ClearSnapshot: false),
        CodexProcessExitedException => new(
            FailureKind.ProcessExited,
            WidgetStatus.Offline,
            "Codex 额度服务暂时不可用，正在重试。",
            ClearSnapshot: false),
        IOException => new(
            FailureKind.Offline,
            WidgetStatus.Offline,
            "网络暂时不可用，正在重试。",
            ClearSnapshot: false),
        _ => new(
            FailureKind.Unknown,
            WidgetStatus.Offline,
            "额度暂时不可用，正在重试。",
            ClearSnapshot: false),
    };

    private sealed record FailureClassification(
        FailureKind Kind,
        WidgetStatus Status,
        string Hint,
        bool ClearSnapshot);

    private sealed class RefreshAttempt(long operationId, long accountGeneration)
    {
        public long OperationId { get; } = operationId;

        public long AccountGeneration { get; set; } = accountGeneration;
    }

    private sealed record ViewDispatch(
        WidgetViewModel ViewModel,
        Action<WidgetViewModel> Action,
        TaskCompletionSource? Completion);
}
