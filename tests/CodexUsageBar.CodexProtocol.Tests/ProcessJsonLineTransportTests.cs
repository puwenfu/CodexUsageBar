using System.Reflection;
using CodexUsageBar.CodexProtocol.Transport;

namespace CodexUsageBar.CodexProtocol.Tests;

public sealed class ProcessJsonLineTransportTests
{
    [Fact]
    public void ResolveCurrentCodexExecutable_PrefersLiveUserRuntimeAfterPackageUpdate()
    {
        const string stalePath =
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.720.1000.0_x64__test\app\resources\codex.exe";
        const string currentPath =
            @"C:\Users\Test\AppData\Local\OpenAI\Codex\bin\current-runtime\codex.exe";

        var resolved = ProcessJsonLineTransport.ResolveCurrentCodexExecutable(
            [stalePath, currentPath],
            path => path is stalePath or currentPath);

        Assert.Equal(currentPath, resolved);
    }

    [Theory]
    [InlineData("codex", true)]
    [InlineData("CoDeX", true)]
    [InlineData("codex.cmd", true)]
    [InlineData("codex.bat", true)]
    [InlineData("unrelated-tool", false)]
    [InlineData("codex.exe", false)]
    public void NeedsCmdWrapper_OnlyWrapsCodexCommandShims(string fileName, bool expected)
    {
        var method = typeof(ProcessJsonLineTransport).GetMethod(
            "NeedsCmdWrapper",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, Assert.IsType<bool>(method.Invoke(null, [fileName])));
    }
}
