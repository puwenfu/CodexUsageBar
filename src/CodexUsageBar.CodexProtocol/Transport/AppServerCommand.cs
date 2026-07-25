namespace CodexUsageBar.CodexProtocol.Transport;

public sealed record AppServerCommand(string FileName, IReadOnlyList<string> Arguments)
{
    public static AppServerCommand InstalledCodex { get; } =
        new("codex", ["app-server", "--listen", "stdio://"]);
}
