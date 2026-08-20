namespace BEngine.Editor;

public static class CompilationPipeline
{
    public static event Action<object>? compilationStarted;
    public static event Action<object>? compilationFinished;
    public static event Action<string>? assemblyCompilationStarted;
    public static event Action<string, CompilerMessage[]>? assemblyCompilationFinished;

    public static void RequestScriptCompilation(
        RequestScriptCompilationOptions options = RequestScriptCompilationOptions.None)
    {
        _ = options;
        if (EditorApplication.isAssemblyReloadLocked)
        {
            EditorApplication.RequestReloadWhenUnlocked();
            return;
        }
        EditorBridge.Host?.RequestScriptCompilation();
    }

    internal static void RaiseCompilationStarted(object context) =>
        EditorCallbackDispatcher.Invoke(compilationStarted, context, nameof(compilationStarted));

    internal static void RaiseCompilationFinished(object context) =>
        EditorCallbackDispatcher.Invoke(compilationFinished, context, nameof(compilationFinished));

    internal static void RaiseAssemblyCompilationStarted(string assemblyPath) =>
        EditorCallbackDispatcher.Invoke(assemblyCompilationStarted, assemblyPath,
            nameof(assemblyCompilationStarted));

    internal static void RaiseAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages) =>
        EditorCallbackDispatcher.Invoke(assemblyCompilationFinished, assemblyPath, messages,
            nameof(assemblyCompilationFinished));
}
