namespace CodexUsageBar.App.Tests;

public sealed class StaTestTests
{
    [Fact]
    public void Run_ThrowsDiagnosticTimeoutInsteadOfHangingTesthost()
    {
        using var release = new ManualResetEventSlim();

        try
        {
            var exception = Assert.Throws<TimeoutException>(() =>
                StaTest.Run(() => release.Wait(), TimeSpan.FromMilliseconds(100)));

            Assert.Contains("STA test action did not complete", exception.Message, StringComparison.Ordinal);
            Assert.Contains("100 ms", exception.Message, StringComparison.Ordinal);
            Assert.Contains("thread id", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            release.Set();
        }
    }
}
