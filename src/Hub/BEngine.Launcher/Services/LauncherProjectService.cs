using System.Buffers;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal sealed class LauncherProjectService
{
    private static readonly SearchValues<char> InvalidFileNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    internal LauncherProjectWorkspace Open(string projectPath) =>
        LauncherProjectWorkspace.Open(Path.GetFullPath(projectPath));

    internal LauncherProjectWorkspace Create(string locationPath, string projectName) =>
        LauncherProjectWorkspace.Create(ResolveTargetPath(locationPath, projectName), projectName);

    internal string ResolveTargetPath(string locationPath, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        var name = projectName.Trim();
        if (name is "." or ".." || name.AsSpan().ContainsAny(InvalidFileNameChars))
            throw new ArgumentException("Project name contains characters that cannot be used in a folder name.",
                nameof(projectName));
        return Path.Combine(Path.GetFullPath(locationPath.Trim()), name);
    }
}
