using System.Reflection;

namespace CodexUsageBar.App.Services;

internal static class AboutVersionProvider
{
    public static string GetDisplayText(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0]
            .Trim();
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return $"版本 v{informational}";
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "版本未知"
            : $"版本 v{version.ToString(3)}";
    }
}
