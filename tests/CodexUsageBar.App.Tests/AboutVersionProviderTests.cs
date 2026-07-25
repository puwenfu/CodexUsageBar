using System.Reflection;
using System.Reflection.Emit;
using CodexUsageBar.App.Services;

namespace CodexUsageBar.App.Tests;

public sealed class AboutVersionProviderTests
{
    [Fact]
    public void GetDisplayText_StripsSourceRevisionMetadata()
    {
        var assembly = BuildAssembly("1.1.3+abcdef", new Version(1, 1, 3, 0));

        Assert.Equal("版本 v1.1.3", AboutVersionProvider.GetDisplayText(assembly));
    }

    [Fact]
    public void GetDisplayText_FallsBackToThreePartAssemblyVersion()
    {
        var assembly = BuildAssembly(null, new Version(1, 1, 3, 0));

        Assert.Equal("版本 v1.1.3", AboutVersionProvider.GetDisplayText(assembly));
    }

    private static Assembly BuildAssembly(string? informationalVersion, Version version)
    {
        var name = new AssemblyName($"AboutVersionTests_{Guid.NewGuid():N}")
        {
            Version = version,
        };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            name,
            AssemblyBuilderAccess.Run);
        if (informationalVersion is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute)
                .GetConstructor([typeof(string)])!;
            assembly.SetCustomAttribute(
                new CustomAttributeBuilder(constructor, [informationalVersion]));
        }

        return assembly;
    }
}
