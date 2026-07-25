namespace CodexUsageBar.CodexProtocol.JsonRpc;

internal sealed class JsonRpcNotificationEventArgs(string method) : EventArgs
{
    public string Method { get; } = method;
}
