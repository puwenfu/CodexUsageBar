namespace CodexUsageBar.App.Services;

public enum FailureKind
{
    ProcessExited,
    CommandMissing,
    SignedOut,
    Incompatible,
    Timeout,
    Offline,
    Unknown,
}

public static class RetryPolicy
{
    public static TimeSpan NextDelay(FailureKind kind, int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        return kind switch
        {
            FailureKind.ProcessExited when attempt == 1 => TimeSpan.FromSeconds(5),
            FailureKind.ProcessExited when attempt == 2 => TimeSpan.FromSeconds(15),
            FailureKind.ProcessExited => TimeSpan.FromSeconds(60),
            FailureKind.CommandMissing => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromSeconds(60),
        };
    }
}
