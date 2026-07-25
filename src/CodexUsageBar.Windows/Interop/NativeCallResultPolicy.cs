namespace CodexUsageBar.Windows.Interop;

internal static class NativeCallResultPolicy
{
    internal static bool PointerResultSucceeded(nint result, int lastError) =>
        result != 0 || lastError == 0;
}
