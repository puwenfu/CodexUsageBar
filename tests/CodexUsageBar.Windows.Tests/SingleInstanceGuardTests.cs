namespace CodexUsageBar.Windows.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_FailsWhileFirstGuardIsAlive_ThenSucceedsAfterDisposal()
    {
        var applicationName = $"CodexUsageBar.Tests.Lifecycle.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(applicationName);

        Assert.NotNull(first);
        Assert.Null(SingleInstanceGuard.TryAcquire(applicationName));

        first.Dispose();

        using var afterDisposal = SingleInstanceGuard.TryAcquire(applicationName);
        Assert.NotNull(afterDisposal);
    }

    [Fact]
    public async Task Dispose_CanRunOnDifferentThread_AndReleasesTheNamedSentinel()
    {
        var applicationName = $"CodexUsageBar.Tests.CrossThread.{Guid.NewGuid():N}";
        var guard = SingleInstanceGuard.TryAcquire(applicationName);
        Assert.NotNull(guard);

        await Task.Run(guard.Dispose);

        using var afterDisposal = SingleInstanceGuard.TryAcquire(applicationName);
        Assert.NotNull(afterDisposal);
    }

    [Fact]
    public async Task Dispose_IsSafeWhenCalledConcurrently_AndReleasesTheNamedSentinel()
    {
        var applicationName = $"CodexUsageBar.Tests.Concurrent.{Guid.NewGuid():N}";
        var guard = SingleInstanceGuard.TryAcquire(applicationName);
        Assert.NotNull(guard);
        using var start = new ManualResetEventSlim(initialState: false);
        var disposals = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                guard.Dispose();
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(disposals);

        using var afterDisposal = SingleInstanceGuard.TryAcquire(applicationName);
        Assert.NotNull(afterDisposal);
    }

    [Fact]
    public void TryAcquire_ReturnsNullWhenAnExistingMutexUsesTheExactObjectName()
    {
        var applicationName = $"CodexUsageBar.Tests.LegacyMutex.{Guid.NewGuid():N}";
        using var existing = new Mutex(
            initiallyOwned: false,
            $@"Local\{applicationName}.Singleton",
            out var createdNew);
        Assert.True(createdNew);

        var exception = Record.Exception(() => SingleInstanceGuard.TryAcquire(applicationName));

        Assert.Null(exception);
        Assert.Null(SingleInstanceGuard.TryAcquire(applicationName));
    }

    [Fact]
    public async Task TryAcquire_ParallelCompetitionHasExactlyOneWinner()
    {
        var applicationName = $"CodexUsageBar.Tests.Race.{Guid.NewGuid():N}";
        using var start = new ManualResetEventSlim(initialState: false);
        var attempts = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return SingleInstanceGuard.TryAcquire(applicationName);
            }))
            .ToArray();

        start.Set();
        var guards = await Task.WhenAll(attempts);
        try
        {
            Assert.Single(guards, guard => guard is not null);
        }
        finally
        {
            foreach (var guard in guards)
            {
                guard?.Dispose();
            }
        }
    }
}
