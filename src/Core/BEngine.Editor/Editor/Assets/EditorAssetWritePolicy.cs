namespace BEngine.Editor;

/// <summary>Shared authoring guard for editor tools and project extensions.</summary>
public static class EditorAssetWritePolicy
{
    public static bool CanWrite => !EditorApplication.isPlayingOrWillChangePlaymode;

    public static void EnsureCanWrite(string operation)
    {
        if (CanWrite) return;
        throw new InvalidOperationException(
            $"{operation} is unavailable while entering, running, or exiting Play Mode.");
    }
}
