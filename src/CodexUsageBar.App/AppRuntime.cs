using CodexUsageBar.App.Diagnostics;

namespace CodexUsageBar.App;

internal sealed class AppRuntime : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IAsyncDisposable _refreshCoordinator;
    private readonly IDisposable _windowHost;
    private readonly IDiagnosticLogger _logger;
    private readonly IDisposable _singleInstance;
    private readonly Action _closeWindow;
    private Task? _disposeTask;

    public AppRuntime(
        IAsyncDisposable refreshCoordinator,
        IDisposable windowHost,
        IDiagnosticLogger logger,
        IDisposable singleInstance,
        Action closeWindow)
    {
        _refreshCoordinator = refreshCoordinator ?? throw new ArgumentNullException(nameof(refreshCoordinator));
        _windowHost = windowHost ?? throw new ArgumentNullException(nameof(windowHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _singleInstance = singleInstance ?? throw new ArgumentNullException(nameof(singleInstance));
        _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        ValueTask coordinatorDisposal;
        try
        {
            coordinatorDisposal = _refreshCoordinator.DisposeAsync();
        }
        catch
        {
            coordinatorDisposal = ValueTask.CompletedTask;
        }

        TryDispose(_windowHost);
        TryInvoke(_closeWindow);
        await TryAwaitAsync(coordinatorDisposal).ConfigureAwait(false);
        TryDispose(_logger);
        TryDispose(_singleInstance);
    }

    private static async Task TryAwaitAsync(ValueTask disposal)
    {
        try
        {
            await disposal.ConfigureAwait(false);
        }
        catch
        {
            // Exit must continue through every owned resource.
        }
    }

    private static void TryDispose(IDisposable resource)
    {
        try
        {
            resource.Dispose();
        }
        catch
        {
            // Exit must continue through every owned resource.
        }
    }

    private static void TryInvoke(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // A closing window cannot prevent process resource cleanup.
        }
    }
}

internal sealed class StartupResourceScope : IAsyncDisposable
{
    private readonly List<object> _resources = [];
    private bool _disposed;

    public T Own<T>(T resource)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (resource is not IDisposable && resource is not IAsyncDisposable)
        {
            throw new ArgumentException("Owned startup resources must be disposable.", nameof(resource));
        }

        _resources.Add(resource);
        return resource;
    }

    public void Forget(object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Remove(resource);
    }

    public void ReleaseAll() => _resources.Clear();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            var resource = _resources[index];
            try
            {
                if (resource is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    ((IDisposable)resource).Dispose();
                }
            }
            catch
            {
                // A partial startup failure must continue unwinding all resources.
            }
        }

        _resources.Clear();
    }
}
