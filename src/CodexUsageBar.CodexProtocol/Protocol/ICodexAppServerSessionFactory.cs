namespace CodexUsageBar.CodexProtocol.Protocol;

internal interface ICodexAppServerSessionFactory
{
    Task<ICodexAppServerSession> StartAsync(CancellationToken cancellationToken);
}

internal interface ICodexAppServerSession : IAsyncDisposable
{
    event EventHandler<CodexAppServerNotificationEventArgs>? NotificationReceived;

    Task<CodexAccount?> ReadAccountAsync(CancellationToken cancellationToken);

    Task<CodexRateLimitsReadResult> ReadRateLimitsAsync(CancellationToken cancellationToken);
}

internal sealed class CodexAppServerNotificationEventArgs(string method) : EventArgs
{
    public string Method { get; } = method;
}
