using BEngine.Build;

namespace BEngine.Editor.Build;

internal static class PlayerWorkflowHostLocator
{
    private static readonly IReadOnlyDictionary<BuildTargetPlatform, (string Folder, string Project)> Hosts =
        new Dictionary<BuildTargetPlatform, (string, string)>
        {
            [BuildTargetPlatform.Windows] = ("Desktop", "BEngine.Player.DesktopHost.csproj"),
            [BuildTargetPlatform.Linux] = ("Desktop", "BEngine.Player.DesktopHost.csproj"),
            [BuildTargetPlatform.MacOS] = ("Desktop", "BEngine.Player.DesktopHost.csproj"),
            [BuildTargetPlatform.Android] = ("Android", "BEngine.Player.AndroidHost.csproj"),
            [BuildTargetPlatform.IOS] = ("iOS", "BEngine.Player.iOSHost.csproj"),
            [BuildTargetPlatform.Web] = ("Web", "BEngine.Player.WebHost.csproj")
        };

    internal static bool TryResolve(BuildTargetDescriptor target, string? overridePath, out string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            projectPath = EditorHostServices.Paths.GetFullPath(overridePath);
            return EditorHostServices.FileSystem.FileExists(projectPath);
        }

        var host = Hosts[target.Platform];
        foreach (var start in new[] { EditorHostServices.Platform.BaseDirectory, EditorHostServices.Platform.CurrentDirectory })
        for (var directory = EditorHostServices.Paths.GetFullPath(start); directory is not null;
             directory = EditorHostServices.Paths.GetDirectoryName(directory))
        {
            foreach (var candidate in new[]
                     {
                         EditorHostServices.Paths.Combine(directory, "src", "Platform", "Player", "Hosts",
                             host.Folder, host.Project),
                         EditorHostServices.Paths.Combine(directory, "Engine", "src", "Platform", "Player", "Hosts",
                             host.Folder, host.Project),
                         EditorHostServices.Paths.Combine(directory, "BuildHosts", host.Folder, host.Project)
                     })
                if (EditorHostServices.FileSystem.FileExists(candidate))
                {
                    projectPath = candidate;
                    return true;
                }
        }

        projectPath = string.Empty;
        return false;
    }

    internal static string Resolve(BuildTargetDescriptor target, string? overridePath)
    {
        if (TryResolve(target, overridePath, out var projectPath)) return projectPath;
        if (!string.IsNullOrWhiteSpace(overridePath))
            throw new FileNotFoundException("The Player Host project was not found.", EditorHostServices.Paths.GetFullPath(overridePath));
        var host = Hosts[target.Platform];
        throw new FileNotFoundException(
            $"Could not locate the {target.Platform} Player Host '{host.Project}'. " +
            "Re-export the engine with BuildHosts or set PlayerBuildRequest.PlayerHostProjectPath.");
    }

    internal static string? ResolveExportedEngineRoot(string hostProject)
    {
        for (var directory = EditorHostServices.Paths.GetDirectoryName(hostProject); directory is not null;
             directory = EditorHostServices.Paths.GetDirectoryName(directory))
        {
            var buildRuntime = EditorHostServices.Paths.Combine(directory, "BuildRuntime");
            if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(buildRuntime, "BEngine.Player.dll"))) return buildRuntime;
            if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(directory, "BEngine.Player.dll"))) return directory;
        }
        var applicationBuildRuntime = EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, "BuildRuntime");
        if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(applicationBuildRuntime, "BEngine.Player.dll")))
            return applicationBuildRuntime;
        return EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, "BEngine.Player.dll"))
            ? EditorHostServices.Platform.BaseDirectory
            : null;
    }

}
