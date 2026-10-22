namespace BEngine.Editor;

public static class AssemblyReloadEvents
{
    public static event Action? beforeAssemblyReload;
    public static event Action? afterAssemblyReload;

    internal static void RaiseBeforeAssemblyReload() =>
        EditorCallbackDispatcher.Invoke(beforeAssemblyReload, nameof(beforeAssemblyReload));

    internal static void RaiseAfterAssemblyReload() =>
        EditorCallbackDispatcher.Invoke(afterAssemblyReload, nameof(afterAssemblyReload));
}
