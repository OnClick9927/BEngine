namespace BEngine.Launcher;

internal static class LauncherInstallationPaths
{
    internal static string ResolveTemporaryDirectory(string installationRoot) =>
        Path.Combine(Path.GetFullPath(installationRoot), "Temp");

    internal static string ResolveRoot(string? baseDirectory = null)
    {
        if (baseDirectory is null && Environment.GetEnvironmentVariable("BENGINE_HUB_DIRECTORY") is { Length: > 0 } hubDirectory)
            return Path.GetFullPath(hubDirectory);
        var executableDirectory = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetDirectoryName(Path.GetFullPath(processPath))
            : null;
        var start = new DirectoryInfo(Path.GetFullPath(
            baseDirectory ?? executableDirectory ?? AppContext.BaseDirectory));

        // A downloaded Hub is a complete bootstrapper. Its directory owns the
        // installation even before the first engine or package is downloaded,
        // including directories called Hub and directories inside a source tree.
        if (File.Exists(Path.Combine(start.FullName, "BEngine.Hub.exe")) ||
            (baseDirectory is null && string.Equals(Path.GetFileName(Environment.ProcessPath),
                "BEngine.Hub.exe", StringComparison.OrdinalIgnoreCase)) ||
            (Directory.Exists(Path.Combine(start.FullName, "BEgine")) &&
             Directory.Exists(Path.Combine(start.FullName, "Packages"))))
            return start.FullName;

        // Retain the former split Hub layout only when its parent really is an
        // installation. An arbitrary directory name must never select a parent.
        if (start.Name.Equals("Hub", StringComparison.OrdinalIgnoreCase) && start.Parent is { } parent &&
            Directory.Exists(Path.Combine(parent.FullName, "BEgine")) &&
            Directory.Exists(Path.Combine(parent.FullName, "Packages")))
            return parent.FullName;

        var siblingEngine = Path.Combine(start.FullName, "Engine");
        if (File.Exists(Path.Combine(siblingEngine, "BEngine.Hub.exe")) ||
            (File.Exists(Path.Combine(start.FullName, "BEngine.Launcher.exe")) &&
             Directory.Exists(Path.Combine(siblingEngine, "BEgine")) &&
             Directory.Exists(Path.Combine(siblingEngine, "Packages"))))
            return siblingEngine;

        // Only a source build searches upward for the development checkout.
        // Empty bootstrap directories are valid installation destinations.
        if (!File.Exists(Path.Combine(start.FullName, "BEngine.Launcher.exe")) &&
            !File.Exists(Path.Combine(start.FullName, "BEngine.Launcher.dll")))
            return start.FullName;

        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Engine", "src", "BEngine.sln")))
                return Path.Combine(directory.FullName, "Engine", "Engine");
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return Path.Combine(directory.FullName, "Engine");
        }

        return start.FullName;
    }
}
