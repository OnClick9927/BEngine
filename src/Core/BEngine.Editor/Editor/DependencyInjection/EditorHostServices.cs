using System.Text;
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

    internal static bool HasExtension(string path) => Paths.GetExtension(path).Length > 0;

    internal static string ChangeExtension(string path, string extension)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(extension);
        if (extension.Length > 0 && extension[0] != '.') extension = "." + extension;
        var currentExtension = Paths.GetExtension(path);
        return path[..(path.Length - currentExtension.Length)] + extension;
    }

    internal static void AppendAllText(string path, string contents)
    {
        using var stream = FileSystem.OpenFile(path, FileMode.Append, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.SequentialScan);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: false);
        writer.Write(contents);
    }
}
