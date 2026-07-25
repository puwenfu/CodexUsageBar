namespace CodexUsageBar.CodexProtocol.Transport;

internal interface IJsonLineTransport : IAsyncDisposable
{
    event EventHandler<int?>? Exited;

    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);

    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}
