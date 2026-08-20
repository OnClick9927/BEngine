namespace BEngine.Editor;

internal static class ConsolePreferences
{
    private const string Prefix = "BEngine.Console.";

    internal static bool Collapse
    {
        get => EditorPrefs.GetBool(Prefix + nameof(Collapse));
        set => EditorPrefs.SetBool(Prefix + nameof(Collapse), value);
    }

    internal static bool ErrorPause
    {
        get => EditorPrefs.GetBool(Prefix + nameof(ErrorPause));
        set => EditorPrefs.SetBool(Prefix + nameof(ErrorPause), value);
    }

    internal static bool ClearOnPlay
    {
        get => EditorPrefs.GetBool(Prefix + nameof(ClearOnPlay), true);
        set => EditorPrefs.SetBool(Prefix + nameof(ClearOnPlay), value);
    }

    internal static bool ClearOnBuild
    {
        get => EditorPrefs.GetBool(Prefix + nameof(ClearOnBuild));
        set => EditorPrefs.SetBool(Prefix + nameof(ClearOnBuild), value);
    }

    internal static bool ClearOnRecompile
    {
        get => EditorPrefs.GetBool(Prefix + nameof(ClearOnRecompile));
        set => EditorPrefs.SetBool(Prefix + nameof(ClearOnRecompile), value);
    }
}
