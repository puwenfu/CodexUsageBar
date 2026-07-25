using CodexUsageBar.CodexProtocol.Protocol;

namespace CodexUsageBar.CodexProtocol.Tests;

public sealed class CodexProtocolClientMetadataTests
{
    [Fact]
    public void Version_MatchesTheProtocolAssemblyProductVersion()
    {
        var assemblyVersion =
            typeof(CodexProtocolClientMetadata).Assembly.GetName().Version;

        Assert.NotNull(assemblyVersion);
        Assert.Equal(assemblyVersion.ToString(3), CodexProtocolClientMetadata.Version);
        Assert.Matches(
            @"^\d+\.\d+\.\d+$",
            CodexProtocolClientMetadata.Version);
    }
}
