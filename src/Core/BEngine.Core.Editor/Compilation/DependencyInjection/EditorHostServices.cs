using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>
/// Compatibility entry point for editor APIs whose public surface predates constructor injection.
/// Every capability is still resolved from the active editor service provider.
/// </summary>
internal static class EditorHostServices
{
    internal static FileUtility FileSystem =>
        EditorServiceAccess.GetRequiredService<FileUtility>();

    internal static PathUtility Paths =>
        EditorServiceAccess.GetRequiredService<PathUtility>();

    internal static PlatformUtility Platform =>
        EditorServiceAccess.GetRequiredService<PlatformUtility>();

    internal static ProcessHostUtility Processes =>
        EditorServiceAccess.GetRequiredService<ProcessHostUtility>();

    internal static WaitUtility Wait =>
        EditorServiceAccess.GetRequiredService<WaitUtility>();

    internal static ExecutionUtility Execution =>
        EditorServiceAccess.GetRequiredService<ExecutionUtility>();

    internal static bool HasExtension(string path) => Paths.HasExtension(path);

    internal static string ChangeExtension(string path, string extension) => Paths.ChangeExtension(path, extension);

    internal static void AppendAllText(string path, string contents) => FileSystem.AppendAllText(path, contents);
}
