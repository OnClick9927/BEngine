namespace BEngine.Editor;

internal static class EditorCommandLineHelp
{
    internal static bool TryHandle(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 1 ||
            !arguments[0].Equals("--help", StringComparison.OrdinalIgnoreCase)) return false;
        Console.WriteLine("Usage: BEngine.Editor.exe <project-path>");
        Console.WriteLine("       BEngine.Editor.exe --build-player <project-path> <output-directory> [target-id] [hot-resource-version]");
        Console.WriteLine("       BEngine.Editor.exe --build-content-update <project-path> <output-directory> [target-id] [hot-resource-version]");
        Console.WriteLine("       BEngine.Editor.exe --set-content-latest <package-directory> <version>");
        return true;
    }
}
