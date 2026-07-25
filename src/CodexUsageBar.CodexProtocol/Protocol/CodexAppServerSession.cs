using System.Text.Json;
using CodexUsageBar.CodexProtocol.JsonRpc;
using CodexUsageBar.CodexProtocol.Transport;

namespace CodexUsageBar.CodexProtocol.Protocol;

internal sealed class CodexAppServerSession : ICodexAppServerSession
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly JsonRpcConnection _connection;
    private readonly TimeSpan _requestTimeout;
    private readonly object _notificationSync = new();
    private readonly object _disposeSync = new();
    private readonly List<CodexAppServerNotificationEventArgs> _pendingNotifications = [];
    private EventHandler<CodexAppServerNotificationEventArgs>? _notificationReceived;
    private Task? _disposeTask;

    private CodexAppServerSession(JsonRpcConnection connection, TimeSpan requestTimeout)
    {
        _connection = connection;
        _requestTimeout = requestTimeout;
        _connection.NotificationReceived += OnNotificationReceived;
    }

    public event EventHandler<CodexAppServerNotificationEventArgs>? NotificationReceived
    {
        add
        {
            if (value is null)
            {
                return;
            }

            CodexAppServerNotificationEventArgs[] pending;
            lock (_notificationSync)
            {
                _notificationReceived += value;
                pending = [.. _pendingNotifications];
                _pendingNotifications.Clear();
            }

            foreach (var eventArgs in pending)
            {
                value(this, eventArgs);
            }
        }
        remove
        {
            lock (_notificationSync)
            {
                _notificationReceived -= value;
            }
        }
    }

    public static async Task<CodexAppServerSession> StartAsync(
        AppServerCommand command,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        var transport = ProcessJsonLineTransport.Start(command);
        var connection = new JsonRpcConnection(transport);
        var session = new CodexAppServerSession(connection, requestTimeout);
        try
        {
            await connection.RequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = CodexProtocolClientMetadata.Name,
                        title = CodexProtocolClientMetadata.Title,
                        version = CodexProtocolClientMetadata.Version,
                    },
                    capabilities = new { experimentalApi = false },
                },
                requestTimeout,
                cancellationToken);
            await connection.NotifyAsync("initialized", null, cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public async Task<CodexAccount?> ReadAccountAsync(CancellationToken cancellationToken)
    {
        var result = await _connection.RequestAsync(
            "account/read",
            new { refreshToken = false },
            _requestTimeout,
            cancellationToken);
        return Deserialize<CodexAccountReadResult>(result).Account;
    }

    public async Task<CodexRateLimitsReadResult> ReadRateLimitsAsync(
        CancellationToken cancellationToken)
    {
        var result = await _connection.RequestAsync(
            "account/rateLimits/read",
            null,
            _requestTimeout,
            cancellationToken);
        return Deserialize<CodexRateLimitsReadResult>(result);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _connection.NotificationReceived -= OnNotificationReceived;
        lock (_notificationSync)
        {
            _notificationReceived = null;
            _pendingNotifications.Clear();
        }

        await _connection.DisposeAsync();
    }

    private static T Deserialize<T>(JsonElement result)
    {
        try
        {
            return result.Deserialize<T>(SerializerOptions)
                ?? throw new CodexProtocolCompatibilityException();
        }
        catch (JsonException)
        {
            throw new CodexProtocolCompatibilityException();
        }
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotificationEventArgs eventArgs)
    {
        EventHandler<CodexAppServerNotificationEventArgs>? handlers;
        var notification = new CodexAppServerNotificationEventArgs(eventArgs.Method);
        lock (_notificationSync)
        {
            handlers = _notificationReceived;
            if (handlers is null)
            {
                _pendingNotifications.Add(notification);
                return;
            }
        }

        handlers(this, notification);
    }
}

internal sealed class CodexAppServerSessionFactory(
    AppServerCommand command,
    TimeSpan requestTimeout) : ICodexAppServerSessionFactory
{
    public Task<ICodexAppServerSession> StartAsync(CancellationToken cancellationToken) =>
        StartCoreAsync(cancellationToken);

    private async Task<ICodexAppServerSession> StartCoreAsync(
        CancellationToken cancellationToken) =>
        await CodexAppServerSession.StartAsync(command, requestTimeout, cancellationToken);
}
