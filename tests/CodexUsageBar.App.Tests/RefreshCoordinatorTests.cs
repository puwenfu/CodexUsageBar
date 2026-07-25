using System.Reflection;
using CodexUsageBar.App.Diagnostics;
using CodexUsageBar.App.Services;
using CodexUsageBar.App.ViewModels;
using CodexUsageBar.CodexProtocol.Protocol;
using CodexUsageBar.CodexProtocol.Transport;
using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Presentation;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.App.Tests;

public sealed class RefreshCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentTriggers_RunOneRefreshAndCoalesceAllPendingTriggersIntoOneFollowUp()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else
            {
                secondEntered.SetResult();
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        await using var coordinator = CreateCoordinator(client, out _);

        var first = coordinator.RequestRefresh(RefreshReason.Manual);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var pending = Enumerable.Range(0, 20)
            .Select(_ => coordinator.RequestRefresh(RefreshReason.Manual))
            .ToArray();

        Assert.Equal(1, client.ActiveCalls);
        Assert.Equal(1, client.MaximumActiveCalls);
        releaseFirst.SetResult();
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAll(pending.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, client.CallCount);
        Assert.Equal(1, client.MaximumActiveCalls);
    }

    [Fact]
    public async Task SynchronousCallbackReentryBeforeRunnerPublication_ProducesExactlyOneFollowUpAndDisposeTracksPump()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var followUpEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFollowUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RefreshCoordinator? coordinator = null;
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 1)
            {
                client!.RaiseInlineAccountChanged();
                client.RaiseRefreshRequested();
                _ = coordinator!.RequestRefresh(RefreshReason.Manual);
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else if (call == 2)
            {
                followUpEntered.SetResult();
                await releaseFollowUp.Task;
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        coordinator = CreateCoordinator(client, out _);

        var initial = coordinator.RequestRefresh(RefreshReason.Manual);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        releaseFirst.SetResult();
        await followUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var disposal = coordinator.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        releaseFollowUp.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        await initial.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, client.CallCount);
        Assert.Equal(0, client.ActiveCalls);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesFromDebugDataChanges()
    {
        var debugViewModel = new DebugViewModel();
        var client = new FakeQuotaClient((_, _) => Task.FromResult(CreateSnapshot(70, 40)));
        var coordinator = CreateCoordinator(client, out var viewModel, debugViewModel: debugViewModel);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal("70%", viewModel.FiveHour.PercentageText);
        debugViewModel.IsEnabled = true;
        Assert.Equal("72%", viewModel.FiveHour.PercentageText);

        await coordinator.DisposeAsync();

        debugViewModel.FiveHourPercentage = 63;

        Assert.Equal("72%", viewModel.FiveHour.PercentageText);
    }

    [Fact]
    public async Task DebugToggle_AfterOfflineFailure_RestoresFailureHintAndLastSnapshot()
    {
        await AssertDebugTogglePreservesFailureAsync(
            new TimeoutException("sensitive"),
            "额度读取超时，正在重试。");
    }

    [Fact]
    public async Task DebugToggle_AfterIncompatibleFailure_RestoresFailureHintAndLastSnapshot()
    {
        await AssertDebugTogglePreservesFailureAsync(
            new CodexProtocolCompatibilityException(),
            "额度接口暂不兼容，请更新本工具。");
    }

    [Fact]
    public async Task DebugToggle_DuringRefresh_RestoresRefreshingDisplay()
    {
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 2)
            {
                secondEntered.SetResult();
                await releaseSecond.Task.WaitAsync(cancellationToken);
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        var debugViewModel = new DebugViewModel();
        await using var coordinator = CreateCoordinator(
            client,
            out var viewModel,
            debugViewModel: debugViewModel);
        await coordinator.RequestRefresh(RefreshReason.Manual);

        var refresh = coordinator.RequestRefresh(RefreshReason.Manual);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(viewModel.IsRefreshing);
        var refreshingTooltip = viewModel.Tooltip;

        debugViewModel.IsEnabled = true;
        Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        debugViewModel.IsEnabled = false;

        Assert.True(viewModel.IsRefreshing);
        Assert.Equal("71%", viewModel.FiveHour.PercentageText);
        Assert.Equal(refreshingTooltip, viewModel.Tooltip);

        releaseSecond.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RepeatedInlineAccountChanged_AcceptsCurrentSnapshotWithoutImmediateThirdRead()
    {
        var unexpectedThirdRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call >= 2)
            {
                await Task.Yield();
                client!.RaiseInlineAccountChanged();
            }

            if (call == 3)
            {
                unexpectedThirdRead.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return call == 1 ? CreateSnapshot(72, 41) : CreateSnapshot(12, 34);
        });
        var coordinator = CreateCoordinator(client, out var viewModel);
        try
        {
            await coordinator.RequestRefresh(RefreshReason.Manual);
            Assert.Equal("72%", viewModel.FiveHour.PercentageText);

            var second = coordinator.RequestRefresh(RefreshReason.Manual);
            var completed = await Task.WhenAny(second, unexpectedThirdRead.Task)
                .WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Same(second, completed);
            await second;
            Assert.Equal(2, client.CallCount);
            Assert.Equal("12%", viewModel.FiveHour.PercentageText);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task InlineAccountChangedWithManualAndNotification_QueuesExactlyOneFollowUp()
    {
        var unexpectedFourthRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RefreshCoordinator? coordinator = null;
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call >= 2)
            {
                client!.RaiseInlineAccountChanged();
            }

            if (call == 2)
            {
                _ = coordinator!.RequestRefresh(RefreshReason.Manual);
                client!.RaiseRefreshRequested();
            }

            if (call == 4)
            {
                unexpectedFourthRead.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        coordinator = CreateCoordinator(client, out var viewModel);
        try
        {
            await coordinator.RequestRefresh(RefreshReason.Manual);
            var second = coordinator.RequestRefresh(RefreshReason.Manual);
            var completed = await Task.WhenAny(second, unexpectedFourthRead.Task)
                .WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Same(second, completed);
            await second;
            Assert.Equal(3, client.CallCount);
            Assert.Equal("73%", viewModel.FiveHour.PercentageText);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalAccountChangedDuringActiveRefresh_IsNotClaimedByActiveAttempt()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var coordinator = CreateCoordinator(client, out var viewModel);
        SynchronizationContext.SetSynchronizationContext(priorContext);
        try
        {
            var refresh = coordinator.RequestRefresh(RefreshReason.Manual);
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            await Task.Run(client.RaiseAccountChanged);
            releaseFirst.SetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(2, client.CallCount);
            Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task DerivedAsyncExternalAccountChanged_IsNotClaimedByCurrentAttempt()
    {
        var externalReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExternal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var externalRaised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 1)
            {
                _ = Task.Run(async () =>
                {
                    externalReady.SetResult();
                    await releaseExternal.Task;
                    client!.RaiseAccountChanged();
                    externalRaised.SetResult();
                });
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var coordinator = CreateCoordinator(client, out var viewModel);
        SynchronizationContext.SetSynchronizationContext(priorContext);
        try
        {
            var refresh = coordinator.RequestRefresh(RefreshReason.Manual);
            await externalReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
            releaseExternal.SetResult();
            await externalRaised.Task.WaitAsync(TimeSpan.FromSeconds(3));
            releaseFirst.SetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(2, client.CallCount);
            Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalAccountChangedBetweenStateCommitAndReadyPublish_NeverRepublishesOldSnapshot()
    {
        var client = new FakeQuotaClient((call, _) =>
            Task.FromResult(call == 1 ? CreateSnapshot(71, 41) : CreateSnapshot(72, 42)));
        await using var coordinator = CreateCoordinator(client, out var viewModel);
        var watchAccountClear = 0;
        var accountClearObserved = 0;
        var staleReadyPublications = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName != nameof(WidgetViewModel.FiveHour)
                || Volatile.Read(ref watchAccountClear) == 0)
            {
                return;
            }

            if (viewModel.FiveHour.PercentageText == "--")
            {
                Volatile.Write(ref accountClearObserved, 1);
            }
            else if (
                Volatile.Read(ref accountClearObserved) != 0
                && viewModel.FiveHour.PercentageText == "71%")
            {
                Interlocked.Increment(ref staleReadyPublications);
            }
        };
        coordinator.BeforeReadyPublish = () =>
        {
            if (Interlocked.Exchange(ref watchAccountClear, 1) == 0)
            {
                client.RaiseAccountChanged();
            }
        };

        await coordinator.RequestRefresh(RefreshReason.Manual)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, client.CallCount);
        Assert.Equal(1, Volatile.Read(ref accountClearObserved));
        Assert.Equal(0, Volatile.Read(ref staleReadyPublications));
        Assert.Equal("72%", viewModel.FiveHour.PercentageText);
    }

    [Fact]
    public async Task QueuedOldReadyCallback_AfterDirectAccountClear_DoesNotRepublishOldSnapshot()
    {
        var client = new FakeQuotaClient((call, _) =>
            Task.FromResult(CreateSnapshot(70 + call, 40 + call)));
        var context = new QueuedSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out var viewModel);
        SynchronizationContext.SetSynchronizationContext(priorContext);
        try
        {
            await Task.Run(() => coordinator.RequestRefresh(RefreshReason.Manual))
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(context.PendingCount >= 2);

            var staleReadyPublications = 0;
            viewModel.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(WidgetViewModel.FiveHour)
                    && viewModel.FiveHour.PercentageText == "71%")
                {
                    staleReadyPublications++;
                }
            };

            var eventContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                client.RaiseAccountChanged();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(eventContext);
            }

            Assert.Equal("72%", viewModel.FiveHour.PercentageText);
            context.DrainAll();

            Assert.Equal(0, staleReadyPublications);
            Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task QueuedOldFailureCallback_AfterDirectAccountClear_DoesNotReplaceNewSnapshot()
    {
        var client = new FakeQuotaClient((call, _) => call == 1
            ? Task.FromException<QuotaSnapshot>(new TimeoutException("sanitized"))
            : Task.FromResult(CreateSnapshot(72, 42)));
        var context = new QueuedSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out var viewModel);
        SynchronizationContext.SetSynchronizationContext(priorContext);
        try
        {
            await Task.Run(() => coordinator.RequestRefresh(RefreshReason.Manual))
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(context.PendingCount >= 2);

            var eventContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                client.RaiseAccountChanged();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(eventContext);
            }

            Assert.Equal("72%", viewModel.FiveHour.PercentageText);
            context.DrainAll();

            Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ActiveAttempt_IsClearedAfterSuccessAndFailure()
    {
        var client = new FakeQuotaClient((call, _) => call == 1
            ? Task.FromResult(CreateSnapshot(72, 41))
            : Task.FromException<QuotaSnapshot>(new TimeoutException("sanitized")));
        await using var coordinator = CreateCoordinator(client, out _);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Null(GetActiveAttempt(coordinator));

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Null(GetActiveAttempt(coordinator));
    }

    [Fact]
    public async Task TriggersDuringRunnerFinalTransition_AreConsumedAndDisposeTracksFollowUp()
    {
        var followUpEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFollowUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 2)
            {
                followUpEntered.SetResult();
                await releaseFollowUp.Task;
            }

            return CreateSnapshot(70 + call, 40 + call);
        });
        var coordinator = CreateCoordinator(client, out _);
        var transitionTriggered = 0;
        coordinator.BeforeRunnerFinalTransition = () =>
        {
            if (Interlocked.Exchange(ref transitionTriggered, 1) != 0)
            {
                return;
            }

            _ = coordinator.RequestRefresh(RefreshReason.Manual);
            client.RaiseRefreshRequested();
            client.RaiseAccountChanged();
        };

        var initial = coordinator.RequestRefresh(RefreshReason.Manual);
        await followUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var disposal = coordinator.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        releaseFollowUp.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        await initial.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, client.CallCount);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task InlineAccountChanged_ClearsOldValuesBeforeCurrentReadCompletes()
    {
        var replacementBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accountRaised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 1)
            {
                return CreateSnapshot(72, 41);
            }

            if (call == 2)
            {
                client!.RaiseInlineAccountChanged();
                accountRaised.SetResult();
                await replacementBlocked.Task.WaitAsync(cancellationToken);
            }
            return CreateSnapshot(12, 34);
        });
        await using var coordinator = CreateCoordinator(client, out var viewModel);
        await coordinator.RequestRefresh(RefreshReason.Manual);

        var refresh = coordinator.RequestRefresh(RefreshReason.Manual);
        await accountRaised.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("--", viewModel.FiveHour.PercentageText);
        Assert.Equal("--", viewModel.Weekly.PercentageText);
        replacementBlocked.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, client.CallCount);
        Assert.Equal("12%", viewModel.FiveHour.PercentageText);
    }

    [Fact]
    public async Task AccountChanged_WaitsForQueuedUiClearBeforeStartingReplacementRead()
    {
        var replacementEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReplacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 2)
            {
                replacementEntered.SetResult();
                await releaseReplacement.Task.WaitAsync(cancellationToken);
            }

            return call == 1 ? CreateSnapshot(72, 41) : CreateSnapshot(12, 34);
        });
        var context = new QueuedSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out var viewModel);
        SynchronizationContext.SetSynchronizationContext(priorContext);
        try
        {
            await coordinator.RequestRefresh(RefreshReason.Manual);
            context.DrainAll();
            Assert.Equal("72%", viewModel.FiveHour.PercentageText);

            var accountChange = Task.Run(client.RaiseAccountChanged);
            await WaitUntilAsync(() => context.PendingCount >= 1);

            Assert.False(replacementEntered.Task.IsCompleted);
            context.DrainOne();
            await accountChange.WaitAsync(TimeSpan.FromSeconds(3));
            await replacementEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("--", viewModel.FiveHour.PercentageText);

            releaseReplacement.SetResult();
            await WaitUntilAsync(() => client.ActiveCalls == 0);
            context.DrainAll();
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task InlineAccountChanged_BlocksRateLimitReadUntilQueuedUiClearCompletes()
    {
        var rateLimitReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient(async (call, cancellationToken) =>
        {
            if (call == 2)
            {
                client!.RaiseInlineAccountChanged();
                rateLimitReadStarted.SetResult();
                await releaseRead.Task.WaitAsync(cancellationToken);
            }

            return call == 1 ? CreateSnapshot(72, 41) : CreateSnapshot(12, 34);
        });
        var context = new QueuedSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out var viewModel);
        SynchronizationContext.SetSynchronizationContext(priorContext);
        try
        {
            await Task.Run(() => coordinator.RequestRefresh(RefreshReason.Manual));
            context.DrainAll();
            Assert.Equal("72%", viewModel.FiveHour.PercentageText);

            var refresh = Task.Run(() => coordinator.RequestRefresh(RefreshReason.Manual));
            await WaitUntilAsync(() => context.PendingCount >= 2);

            Assert.False(rateLimitReadStarted.Task.IsCompleted);
            context.DrainAll();
            await rateLimitReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("--", viewModel.FiveHour.PercentageText);

            releaseRead.SetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(3));
            context.DrainAll();
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClientRefreshInvocation_DoesNotRunInsideCoordinatorStateLock()
    {
        object? stateSync = null;
        var stateLockWasHeld = true;
        var client = new FakeQuotaClient((_, _) =>
        {
            stateLockWasHeld = Monitor.IsEntered(stateSync!);
            return Task.FromResult(CreateSnapshot(72, 41));
        });
        await using var coordinator = CreateCoordinator(client, out _);
        stateSync = typeof(RefreshCoordinator)
            .GetField("_stateSync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator);

        await coordinator.RequestRefresh(RefreshReason.Manual);

        Assert.False(stateLockWasHeld);
    }

    [Fact]
    public void AccountChanged_OnCapturedUiThreadWithDifferentContextInstance_ClearsDirectly() => StaTest.Run(
        () =>
        {
            var releaseRefresh = new TaskCompletionSource<QuotaSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new FakeQuotaClient((_, cancellationToken) =>
                releaseRefresh.Task.WaitAsync(cancellationToken));
            var capturedContext = new RecordingSynchronizationContext();
            var priorContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(capturedContext);
            var coordinator = CreateCoordinator(client, out var viewModel);
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            try
            {
                client.RaiseAccountChanged();

                Assert.Equal("--", viewModel.FiveHour.PercentageText);
                Assert.Equal(0, capturedContext.PostCalls);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(priorContext);
                DispatcherTaskPump.Wait(
                    coordinator.DisposeAsync().AsTask(),
                    System.Windows.Threading.Dispatcher.CurrentDispatcher);
            }
        },
        timeout: TimeSpan.FromSeconds(3));

    [Fact]
    public async Task Dispose_UnblocksInlineAccountHandlerWhenUiQueueNeverDrains()
    {
        FakeQuotaClient? client = null;
        client = new FakeQuotaClient((_, _) =>
        {
            client!.RaiseInlineAccountChanged();
            return Task.FromResult(CreateSnapshot(72, 41));
        });
        var context = new QueuedSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out _);
        SynchronizationContext.SetSynchronizationContext(priorContext);

        var refresh = Task.Run(() => coordinator.RequestRefresh(RefreshReason.Manual));
        await WaitUntilAsync(() => context.PendingCount >= 2);
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task Dispose_UnblocksAccountClearBarrierWhenUiQueueStops()
    {
        var client = new FakeQuotaClient((_, _) => Task.FromResult(CreateSnapshot(72, 41)));
        var context = new QueuedSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out _);
        SynchronizationContext.SetSynchronizationContext(priorContext);

        var accountChange = Task.Run(client.RaiseAccountChanged);
        await WaitUntilAsync(() => context.PendingCount >= 1);
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await accountChange.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, client.CallCount);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task AccountChanged_WhenUiPostFails_DoesNotThrowOrLeaveDisposalBlocked()
    {
        var client = new FakeQuotaClient((_, _) => Task.FromResult(CreateSnapshot(72, 41)));
        var context = new ThrowingSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        var coordinator = CreateCoordinator(client, out _);
        SynchronizationContext.SetSynchronizationContext(priorContext);

        var exception = await Record.ExceptionAsync(() => Task.Run(client.RaiseAccountChanged));
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Null(exception);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task TimeoutAndIncompatibleErrors_PreserveLastSuccessfulSnapshot()
    {
        var client = new FakeQuotaClient((call, _) => call switch
        {
            1 => Task.FromResult(CreateSnapshot(72, 41)),
            2 => Task.FromException<QuotaSnapshot>(new TimeoutException("sensitive")),
            _ => Task.FromException<QuotaSnapshot>(new CodexProtocolCompatibilityException()),
        });
        await using var coordinator = CreateCoordinator(client, out var viewModel);
        await coordinator.RequestRefresh(RefreshReason.Manual);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        Assert.Contains("额度读取超时，正在重试。", viewModel.Tooltip, StringComparison.Ordinal);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        Assert.Contains("额度接口暂不兼容，请更新本工具。", viewModel.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedOut_ClearsLastSnapshot_AndNextSuccessRecovers()
    {
        var client = new FakeQuotaClient((call, _) => call switch
        {
            1 => Task.FromResult(CreateSnapshot(72, 41)),
            2 => Task.FromException<QuotaSnapshot>(new CodexSignedOutException()),
            _ => Task.FromResult(CreateSnapshot(18, 29)),
        });
        await using var coordinator = CreateCoordinator(client, out var viewModel);
        await coordinator.RequestRefresh(RefreshReason.Manual);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal("--", viewModel.FiveHour.PercentageText);
        Assert.Contains("Codex 尚未登录，请先在 Codex 中登录。", viewModel.Tooltip, StringComparison.Ordinal);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal("18%", viewModel.FiveHour.PercentageText);
        Assert.DoesNotContain("尚未登录", viewModel.Tooltip, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(60), coordinator.NextDelay);
        Assert.Equal(0, coordinator.ConsecutiveFailures);
    }

    [Fact]
    public async Task ProcessFailures_BackOffFiveFifteenSixty_ThenSuccessResetsToSixtySeconds()
    {
        var client = new FakeQuotaClient((call, _) => call <= 3
            ? Task.FromException<QuotaSnapshot>(CreateProcessExitedException())
            : Task.FromResult(CreateSnapshot(72, 41)));
        await using var coordinator = CreateCoordinator(client, out _);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal(TimeSpan.FromSeconds(5), coordinator.NextDelay);
        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal(TimeSpan.FromSeconds(15), coordinator.NextDelay);
        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal(TimeSpan.FromSeconds(60), coordinator.NextDelay);
        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Equal(TimeSpan.FromSeconds(60), coordinator.NextDelay);
        Assert.Equal(0, coordinator.ConsecutiveFailures);
    }

    [Fact]
    public async Task StartAsync_RefreshesImmediatelyThenSchedulesExactlySixtySecondsAfterSuccess()
    {
        var delay = new ControlledDelay();
        var client = new FakeQuotaClient((_, _) => Task.FromResult(CreateSnapshot(72, 41)));
        await using var coordinator = CreateCoordinator(client, out _, delay);

        await coordinator.StartAsync();
        var scheduled = await delay.NextDelay.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, client.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(60), scheduled);
    }

    [Fact]
    public async Task ManualResult_CancelsOldWaitAndReschedulesFromLatestFailureOrSuccess()
    {
        var delay = new SequenceDelay();
        var client = new FakeQuotaClient((call, _) => call switch
        {
            1 => Task.FromResult(CreateSnapshot(72, 41)),
            2 => Task.FromException<QuotaSnapshot>(CreateProcessExitedException()),
            _ => Task.FromResult(CreateSnapshot(18, 29)),
        });
        await using var coordinator = CreateCoordinator(client, out _, delay);
        await coordinator.StartAsync();
        var initial = await delay.TakeAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), initial.Delay);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        var retry = await delay.TakeAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(initial.CancellationToken.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(5), retry.Delay);

        await coordinator.RequestRefresh(RefreshReason.Manual);
        var recovered = await delay.TakeAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(retry.CancellationToken.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(60), recovered.Delay);
    }

    [Fact]
    public async Task Dispose_CancelsTimerAndClientAndIsSafeDuringCallbackReentry()
    {
        var delay = new ControlledDelay();
        var client = new FakeQuotaClient((_, _) => Task.FromResult(CreateSnapshot(72, 41)));
        await using var coordinator = CreateCoordinator(client, out _, delay);
        client.RefreshRequested += (_, _) => _ = coordinator.RequestRefresh(RefreshReason.ProtocolNotification);
        await coordinator.StartAsync();
        await delay.NextDelay.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var disposals = Enumerable.Range(0, 8).Select(_ => coordinator.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposals).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(client.IsDisposed);
        Assert.True(delay.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_DuringInFlightRefreshCancelsWorkAndDropsCallbackPendingBitWithoutDeadlock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateSnapshot(72, 41);
        });
        await using var coordinator = CreateCoordinator(client, out _);
        var refresh = coordinator.RequestRefresh(RefreshReason.Manual);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        client.RaiseRefreshRequested();
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, client.CallCount);
        Assert.True(client.IsDisposed);
        Assert.Null(GetActiveAttempt(coordinator));
    }

    [Fact]
    public void CrossThreadCompletion_PostsViewUpdateWithoutSynchronousContextSend() => StaTest.Run(
        () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new FakeQuotaClient(async (_, cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return CreateSnapshot(72, 41);
            });
            var context = new RecordingSynchronizationContext();
            var priorContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            RefreshCoordinator? coordinator = null;
            try
            {
                coordinator = CreateCoordinator(client, out _);
                var refresh = coordinator.RequestRefresh(RefreshReason.Manual);
                release.SetResult();

                DispatcherTaskPump.Wait(refresh, System.Windows.Threading.Dispatcher.CurrentDispatcher);

                Assert.True(refresh.IsCompletedSuccessfully);
                Assert.True(context.PostCalls > 0);
                Assert.Equal(0, context.SendCalls);
            }
            finally
            {
                if (coordinator is not null)
                {
                    DispatcherTaskPump.Wait(
                        coordinator.DisposeAsync().AsTask(),
                        System.Windows.Threading.Dispatcher.CurrentDispatcher);
                }

                SynchronizationContext.SetSynchronizationContext(priorContext);
            }
        },
        timeout: TimeSpan.FromSeconds(3));

    private static RefreshCoordinator CreateCoordinator(
        FakeQuotaClient client,
        out WidgetViewModel viewModel,
        IAsyncDelay? delay = null,
        DebugViewModel? debugViewModel = null)
    {
        var clock = new FixedClock(Now);
        var presentation = new QuotaPresentationService(clock, TimeZoneInfo.Utc);
        viewModel = new WidgetViewModel(
            presentation.Create(null, WidgetStatus.Ready, null, null),
            36);
        var coordinator = new RefreshCoordinator(
            client,
            presentation,
            clock,
            NullDiagnosticLogger.Instance,
            debugViewModel ?? new DebugViewModel(),
            delay ?? new ControlledDelay());
        coordinator.Attach(viewModel);
        return coordinator;
    }

    private static async Task AssertDebugTogglePreservesFailureAsync(
        Exception failure,
        string expectedHint)
    {
        var client = new FakeQuotaClient((call, _) => call == 1
            ? Task.FromResult(CreateSnapshot(72, 41))
            : Task.FromException<QuotaSnapshot>(failure));
        var debugViewModel = new DebugViewModel();
        await using var coordinator = CreateCoordinator(
            client,
            out var viewModel,
            debugViewModel: debugViewModel);
        await coordinator.RequestRefresh(RefreshReason.Manual);
        await coordinator.RequestRefresh(RefreshReason.Manual);
        Assert.Contains(expectedHint, viewModel.Tooltip, StringComparison.Ordinal);
        var failureTooltip = viewModel.Tooltip;

        debugViewModel.IsEnabled = true;
        Assert.Equal("调试模式模拟数据", viewModel.Tooltip);
        debugViewModel.IsEnabled = false;

        Assert.Equal("72%", viewModel.FiveHour.PercentageText);
        Assert.Equal(failureTooltip, viewModel.Tooltip);
        Assert.Contains(expectedHint, viewModel.Tooltip, StringComparison.Ordinal);
    }

    private static QuotaSnapshot CreateSnapshot(int fiveHour, int weekly) => new(
        new QuotaWindow(QuotaWindowKind.FiveHour, 300, 100 - fiveHour, fiveHour, Now.AddHours(2)),
        new QuotaWindow(QuotaWindowKind.Weekly, 10_080, 100 - weekly, weekly, Now.AddDays(2)),
        Now);

    private static CodexProcessExitedException CreateProcessExitedException() =>
        (CodexProcessExitedException)Activator.CreateInstance(
            typeof(CodexProcessExitedException),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [1],
            culture: null)!;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the deterministic test condition.");
            await Task.Yield();
        }
    }

    private static object? GetActiveAttempt(RefreshCoordinator coordinator) =>
        typeof(RefreshCoordinator)
            .GetField("_activeAttempt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class ControlledDelay : IAsyncDelay
    {
        public TaskCompletionSource<TimeSpan> NextDelay { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken LastCancellationToken { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            NextDelay.TrySetResult(delay);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class SequenceDelay : IAsyncDelay
    {
        private readonly Queue<DelayRequest> _requests = [];
        private readonly SemaphoreSlim _available = new(0);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Enqueue(new DelayRequest(delay, cancellationToken));
            }

            _available.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public async Task<DelayRequest> TakeAsync()
        {
            await _available.WaitAsync();
            lock (_requests)
            {
                return _requests.Dequeue();
            }
        }
    }

    private sealed record DelayRequest(TimeSpan Delay, CancellationToken CancellationToken);

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostCalls { get; private set; }

        public int SendCalls { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCalls++;
            callback(state);
        }

        public override void Send(SendOrPostCallback callback, object? state)
        {
            SendCalls++;
            callback(state);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];

        public int PendingCount
        {
            get
            {
                lock (_callbacks)
                {
                    return _callbacks.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_callbacks)
            {
                _callbacks.Enqueue((callback, state));
            }
        }

        public void DrainOne()
        {
            (SendOrPostCallback Callback, object? State) callback;
            lock (_callbacks)
            {
                callback = _callbacks.Dequeue();
            }

            callback.Callback(callback.State);
        }

        public void DrainAll()
        {
            while (true)
            {
                lock (_callbacks)
                {
                    if (_callbacks.Count == 0)
                    {
                        return;
                    }
                }

                DrainOne();
            }
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            throw new InvalidOperationException("Dispatcher is shutting down.");
    }

    private sealed class FakeQuotaClient(
        Func<int, CancellationToken, Task<QuotaSnapshot>> refresh) : ICodexQuotaClient
    {
        private int _activeCalls;
        private int _callCount;
        private int _maximumActiveCalls;

        private long _activeOperationId;

        public event EventHandler<CodexAccountChangedEventArgs>? AccountChanged;
        public event EventHandler? RefreshRequested;

        public int ActiveCalls => Volatile.Read(ref _activeCalls);
        public int CallCount => Volatile.Read(ref _callCount);
        public int MaximumActiveCalls => Volatile.Read(ref _maximumActiveCalls);
        public bool IsDisposed { get; private set; }

        public async Task<QuotaSnapshot> RefreshAsync(
            long refreshOperationId,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            var active = Interlocked.Increment(ref _activeCalls);
            InterlockedExtensions.Max(ref _maximumActiveCalls, active);
            Volatile.Write(ref _activeOperationId, refreshOperationId);
            try
            {
                return await refresh(call, cancellationToken);
            }
            finally
            {
                Interlocked.CompareExchange(ref _activeOperationId, 0, refreshOperationId);
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseInlineAccountChanged() =>
            AccountChanged?.Invoke(
                this,
                new CodexAccountChangedEventArgs(Volatile.Read(ref _activeOperationId)));

        public void RaiseAccountChanged() =>
            AccountChanged?.Invoke(this, new CodexAccountChangedEventArgs(refreshOperationId: null));
        public void RaiseRefreshRequested() => RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var prior = Interlocked.CompareExchange(ref location, value, current);
                if (prior == current)
                {
                    return;
                }

                current = prior;
            }
        }
    }
}
