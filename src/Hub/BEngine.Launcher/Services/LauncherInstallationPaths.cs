namespace BEngine.Launcher;

internal static class LauncherInstallationPaths
{
    internal static string ResolveRoot(string? baseDirectory = null)
    {
        var executableDirectory = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetDirectoryName(Path.GetFullPath(processPath))
            : null;
        var start = new DirectoryInfo(Path.GetFullPath(
            baseDirectory ?? executableDirectory ?? AppContext.BaseDirectory));
        if (start.Name.Equals("Hub", StringComparison.OrdinalIgnoreCase) && start.Parent is not null)
            return start.Parent.FullName;

        // Exported distributions keep the single-file Hub inside the Engine
        // distribution directory. Resolve that layout before falling back to
        // source-tree discovery so BEgine and Packages remain visible.
        if (File.Exists(Path.Combine(start.FullName, "BEngine.Hub.exe")) ||
            (Directory.Exists(Path.Combine(start.FullName, "BEgine")) &&
             Directory.Exists(Path.Combine(start.FullName, "Packages"))))
            return start.FullName;
        var siblingEngine = Path.Combine(start.FullName, "Engine");
        if (File.Exists(Path.Combine(siblingEngine, "BEngine.Hub.exe")) ||
            (File.Exists(Path.Combine(start.FullName, "BEngine.Launcher.exe")) &&
             Directory.Exists(Path.Combine(siblingEngine, "BEgine")) &&
             Directory.Exists(Path.Combine(siblingEngine, "Packages"))))
            return siblingEngine;

        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Engine", "src", "BEngine.sln")))
                return Path.Combine(directory.FullName, "Engine", "Engine");
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return Path.Combine(directory.FullName, "Engine");
        }

        return start.Parent?.FullName ?? start.FullName;
    }
}
