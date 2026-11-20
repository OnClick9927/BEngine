namespace BEngine.Editor;

/// <summary>Optional host coordination; compilation and package management also work without a UI.</summary>
public static class EditorToolingLifecycle
{
    public static event Action? PackageMutationRequested;
    public static event Action? ScriptCompilationRequested;
    internal static void PreparePackageMutation() => PackageMutationRequested?.Invoke();
    internal static void RequestScriptCompilation() => ScriptCompilationRequested?.Invoke();
}
