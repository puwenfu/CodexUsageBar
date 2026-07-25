namespace CodexUsageBar.CodexProtocol.Protocol;

internal static class CodexProtocolClientMetadata
{
    public const string Name = "CodexUsageBar";

    public const string Title = "Codex Usage Bar";

    public static string Version { get; } =
        typeof(CodexProtocolClientMetadata).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException(
            "The Codex protocol assembly does not define a product version.");
}
