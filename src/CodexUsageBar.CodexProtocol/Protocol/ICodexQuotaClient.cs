using CodexUsageBar.Core.Models;

namespace CodexUsageBar.CodexProtocol.Protocol;

public sealed class CodexAccountChangedEventArgs(long? refreshOperationId) : EventArgs
{
    public long? RefreshOperationId { get; } = refreshOperationId;
}

public interface ICodexQuotaClient : IAsyncDisposable
{
    event EventHandler<CodexAccountChangedEventArgs>? AccountChanged;

    event EventHandler? RefreshRequested;

    Task<QuotaSnapshot> RefreshAsync(
        long refreshOperationId,
        CancellationToken cancellationToken);
}
