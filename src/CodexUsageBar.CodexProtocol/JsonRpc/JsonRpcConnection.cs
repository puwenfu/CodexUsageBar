using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using CodexUsageBar.CodexProtocol.Transport;

namespace CodexUsageBar.CodexProtocol.JsonRpc;

internal sealed class JsonRpcConnection : IAsyncDisposable
{
    [ThreadStatic]
    private static JsonRpcConnection? _dispatchingNotificationFor;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IJsonLineTransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<JsonRpcNotificationEventArgs> _notifications =
        Channel.CreateUnbounded<JsonRpcNotificationEventArgs>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    private readonly CancellationTokenSource _readerCancellation = new();
    private readonly CancellationToken _shutdownToken;
    private readonly object _disposeSync = new();
    private readonly Task _readerTask;
    private readonly Task _notificationDispatcherTask;
    private Task? _disposeTask;
    private int? _transportExitCode;
    private long _nextRequestId;
    private int _disposed;

    public JsonRpcConnection(IJsonLineTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _shutdownToken = _readerCancellation.Token;
        _transport.Exited += OnTransportExited;
        _notificationDispatcherTask = DispatchNotificationsAsync();
        _readerTask = ReadLoopAsync();
    }

    public event EventHandler<JsonRpcNotificationEventArgs>? NotificationReceived;

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token,
            _shutdownToken);

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("A JSON-RPC request id was reused.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            _pending.TryRemove(id, out _);
            completion.TrySetCanceled(_shutdownToken);
            return await completion.Task;
        }

        try
        {
            var line = JsonSerializer.Serialize(
                new RequestMessage(id, method, parameters),
                SerializerOptions);
            await _transport.WriteLineAsync(line, requestCancellation.Token);
            return await completion.Task.WaitAsync(requestCancellation.Token);
        }
        catch (Exception) when (completion.Task.IsCanceled)
        {
            return await completion.Task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException("The Codex app-server JSON-RPC request timed out.");
        }
        catch (CodexProcessExitedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw SanitizeTransportFailure(exception);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task NotifyAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        try
        {
            var line = JsonSerializer.Serialize(
                new NotificationMessage(method, parameters),
                SerializerOptions);
            await _transport.WriteLineAsync(line, cancellationToken);
        }
        catch (CodexProcessExitedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw SanitizeTransportFailure(exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(_dispatchingNotificationFor, this))
        {
            return new ValueTask(Task.FromException(new InvalidOperationException(
                "The JSON-RPC connection cannot be disposed synchronously from its notification handler.")));
        }

        TaskCompletionSource? starter = null;
        Task disposalTask;
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposed, 1);
                starter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = starter.Task;
            }

            disposalTask = _disposeTask;
        }

        if (starter is not null)
        {
            _ = CompleteDisposeAsync(starter);
        }

        return new ValueTask(disposalTask);
    }

    private async Task DisposeCoreAsync()
    {
        _transport.Exited -= OnTransportExited;
        _readerCancellation.Cancel();
        CancelPendingRequests();
        try
        {
            await _transport.DisposeAsync();
        }
        finally
        {
            try
            {
                await _readerTask;
            }
            catch (OperationCanceledException)
            {
            }

            _notifications.Writer.TryComplete();
            await _notificationDispatcherTask;
            _readerCancellation.Dispose();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var line in _transport.ReadLinesAsync(_readerCancellation.Token))
            {
                RouteMessage(line);
            }

            if (!_readerCancellation.IsCancellationRequested)
            {
                FailPendingRequests(new CodexProcessExitedException(_transportExitCode));
            }
        }
        catch (OperationCanceledException) when (_readerCancellation.IsCancellationRequested)
        {
        }
        catch (CodexProcessExitedException exception)
        {
            FailPendingRequests(exception);
        }
        catch
        {
            FailPendingRequests(new CodexJsonRpcException(
                "The Codex app-server returned an invalid JSON-RPC message."));
        }
        finally
        {
            _notifications.Writer.TryComplete();
        }
    }

    private void RouteMessage(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (root.TryGetProperty("id", out var idElement)
            && idElement.ValueKind == JsonValueKind.Number
            && idElement.TryGetInt64(out var id)
            && _pending.TryGetValue(id, out var completion))
        {
            if (root.TryGetProperty("error", out _))
            {
                completion.TrySetException(new CodexJsonRpcException(
                    "The Codex app-server rejected the JSON-RPC request."));
            }
            else if (root.TryGetProperty("result", out var result))
            {
                completion.TrySetResult(result.Clone());
            }
            else
            {
                completion.TrySetException(new CodexJsonRpcException(
                    "The Codex app-server returned an incomplete JSON-RPC response."));
            }

            return;
        }

        if (!root.TryGetProperty("id", out _)
            && root.TryGetProperty("method", out var methodElement)
            && methodElement.ValueKind == JsonValueKind.String
            && methodElement.GetString() is { } method)
        {
            _notifications.Writer.TryWrite(new JsonRpcNotificationEventArgs(method));
        }
    }

    private async Task DispatchNotificationsAsync()
    {
        await foreach (var eventArgs in _notifications.Reader.ReadAllAsync())
        {
            foreach (EventHandler<JsonRpcNotificationEventArgs> handler
                     in NotificationReceived?.GetInvocationList() ?? [])
            {
                var previousDispatch = _dispatchingNotificationFor;
                try
                {
                    _dispatchingNotificationFor = this;
                    handler(this, eventArgs);
                }
                catch
                {
                    // Consumer event handlers cannot stop response demultiplexing.
                }
                finally
                {
                    _dispatchingNotificationFor = previousDispatch;
                }
            }
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await Task.Yield();
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private void OnTransportExited(object? sender, int? exitCode)
    {
        _transportExitCode = exitCode;
    }

    private void CancelPendingRequests()
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetCanceled(_shutdownToken);
        }
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static CodexJsonRpcException SanitizeTransportFailure(Exception exception) =>
        exception as CodexJsonRpcException
        ?? new CodexJsonRpcException("The Codex app-server JSON-RPC transport failed.");

    private sealed record RequestMessage(
        long Id,
        string Method,
        [property: JsonPropertyName("params")] object? Parameters);

    private sealed record NotificationMessage(
        string Method,
        [property: JsonPropertyName("params")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        object? Parameters);
}
