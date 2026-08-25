namespace BEngine.Editor;

internal static class EditorAssetWritePolicy
{
    internal static bool CanWrite => !EditorApplication.isPlayingOrWillChangePlaymode;

    internal static void EnsureCanWrite(string operation)
    {
        if (CanWrite) return;
        throw new InvalidOperationException(
            $"{operation} is unavailable while entering, running, or exiting Play Mode.");
    }
}
