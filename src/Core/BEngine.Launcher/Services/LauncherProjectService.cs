using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Launcher;

internal sealed class LauncherProjectService
{
    internal ProjectWorkspace Open(string projectPath) =>
        ProjectWorkspace.Open(Path.GetFullPath(projectPath));

    internal ProjectWorkspace Create(string locationPath, string projectName) =>
        ProjectWorkspaceFactory.Create(ResolveTargetPath(locationPath, projectName), projectName);

    internal string ResolveTargetPath(string locationPath, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        var name = projectName.Trim();
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Project name contains characters that cannot be used in a folder name.",
                nameof(projectName));
        return Path.Combine(Path.GetFullPath(locationPath.Trim()), name);
    }
}
