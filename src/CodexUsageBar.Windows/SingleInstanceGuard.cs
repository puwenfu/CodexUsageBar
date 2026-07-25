namespace CodexUsageBar.Windows;

public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _sentinel;

    private SingleInstanceGuard(Mutex sentinel)
    {
        _sentinel = sentinel;
    }

    public static SingleInstanceGuard? TryAcquire(string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var sentinel = new Mutex(
            initiallyOwned: false,
            $@"Local\{applicationName}.Singleton",
            out var createdNew);
        if (!createdNew)
        {
            sentinel.Dispose();
            return null;
        }

        return new SingleInstanceGuard(sentinel);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _sentinel, null)?.Dispose();
    }
}
