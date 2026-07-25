namespace CodexUsageBar.CodexProtocol.Transport;

public sealed class CodexCommandNotFoundException : Exception
{
    internal CodexCommandNotFoundException(string commandName)
        : base($"The Codex command '{commandName}' could not be started.")
    {
    }
}

public sealed class CodexProcessExitedException : Exception
{
    internal CodexProcessExitedException(int? exitCode)
        : base(exitCode is null
            ? "The Codex app-server process exited before completing the request."
            : $"The Codex app-server process exited with code {exitCode} before completing the request.")
    {
        ExitCode = exitCode;
    }

    public int? ExitCode { get; }
}

public sealed class CodexJsonRpcException : Exception
{
    internal CodexJsonRpcException(string message)
        : base(message)
    {
    }
}
