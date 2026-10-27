using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal static class LauncherProjectEngineResolver
{
    internal static string Resolve(string engineVersion, IReadOnlyList<LauncherEngineInfo> engines,
        string configuredEditorPath)
    {
        BPackageVersion.Validate(engineVersion);
        ArgumentNullException.ThrowIfNull(engines);
        var engine = engines.FirstOrDefault(candidate =>
            string.Equals(candidate.Version, engineVersion, StringComparison.Ordinal));
        if (engine is not null)
        {
            var editor = Path.GetFullPath(Path.Combine(engine.DirectoryPath, "BEngine.Editor.exe"));
            if (File.Exists(editor)) return editor;
            throw new FileNotFoundException($"The editor for BEngine {engineVersion} was not found at '{editor}'.", editor);
        }

        // A Hub built from source can launch its matching composed Editor host.
        // Installed version folders are always resolved through the catalog, so
        // an explicit/default path cannot silently select another release.
        if (string.Equals(engineVersion, EngineVersion.Current, StringComparison.Ordinal) &&
            IsDevelopmentHost(configuredEditorPath))
            return Path.GetFullPath(configuredEditorPath);

        throw new InvalidOperationException($"BEngine {engineVersion} is not installed. Install this engine version before opening the project.");
    }

    private static bool IsDevelopmentHost(string configuredEditorPath)
    {
        if (string.IsNullOrWhiteSpace(configuredEditorPath)) return false;
        var editor = Path.GetFullPath(configuredEditorPath);
        if (!File.Exists(editor) ||
            !(Path.GetFileName(editor).Equals("BEngine.Editor.exe", StringComparison.OrdinalIgnoreCase) ||
              Path.GetFileName(editor).Equals("BEngine.Editor.Host.exe", StringComparison.OrdinalIgnoreCase)))
            return false;

        var editorDirectory = new DirectoryInfo(Path.GetDirectoryName(editor)!);
        for (var directory = editorDirectory; directory is not null; directory = directory.Parent)
            if (directory.Name.StartsWith("BEgine-", StringComparison.OrdinalIgnoreCase))
                return false;

        if ((File.Exists(Path.Combine(editorDirectory.FullName, "BEngine.Launcher.exe")) ||
             File.Exists(Path.Combine(editorDirectory.FullName, "BEngine.Launcher.dll"))) &&
            File.Exists(Path.Combine(editorDirectory.FullName, "BEngine.dll")) &&
            (File.Exists(Path.Combine(editorDirectory.FullName, "BEngine.Editor.dll")) ||
             File.Exists(Path.Combine(editorDirectory.FullName, "BEngine.Editor.Host.dll"))))
            return true;

        for (var directory = editorDirectory; directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Engine", "src", "BEngine.sln"))) continue;
            var output = Path.Combine(directory.FullName, "Temp", "Engine", "Build", "bin", "BEngine.Editor.Host") + Path.DirectorySeparatorChar;
            return editor.StartsWith(output, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
