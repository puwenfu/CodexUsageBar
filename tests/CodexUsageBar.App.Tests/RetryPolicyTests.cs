using CodexUsageBar.App.Services;

namespace CodexUsageBar.App.Tests;

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(FailureKind.ProcessExited, 1, 5)]
    [InlineData(FailureKind.ProcessExited, 2, 15)]
    [InlineData(FailureKind.ProcessExited, 3, 60)]
    [InlineData(FailureKind.ProcessExited, 9, 60)]
    [InlineData(FailureKind.CommandMissing, 1, 300)]
    public void NextDelay_ReturnsContractSeconds(FailureKind kind, int attempt, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), RetryPolicy.NextDelay(kind, attempt));
    }
}
