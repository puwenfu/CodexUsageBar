namespace CodexUsageBar.CodexProtocol.Protocol;

public sealed class CodexSignedOutException : Exception
{
    public CodexSignedOutException()
        : base("Codex is signed out.")
    {
    }
}

public sealed class CodexProtocolCompatibilityException : Exception
{
    public CodexProtocolCompatibilityException()
        : base("The Codex app-server protocol is not compatible with this application.")
    {
    }
}
