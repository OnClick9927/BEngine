using BEngine.Platform;
using BEngine.Tooling;

namespace BEngine.Editor;

/// <summary>Editor Play Mode data belongs to this installation, not the exported game's profile.</summary>
internal sealed class EditorApplicationPaths(IPlatformService platform, IPathService paths) : IApplicationPaths
{
    public string BaseDirectory => platform.BaseDirectory;
    public string CurrentDirectory => platform.CurrentDirectory;
    public string TemporaryDirectory => ToolEnvironment.SystemTemporaryDirectory;
    public string? ProcessPath => platform.ProcessPath;

    public string GetPersistentDataDirectory(string companyName, string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        var root = paths.Combine(ToolEnvironment.TempDirectory, "PlayMode");
        var destination = paths.GetFullPath(paths.Combine(root, companyName, productName));
        var relative = paths.GetRelativePath(root, destination);
        if (paths.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{paths.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new ArgumentException("Editor Play Mode data must remain inside the Hub's Temp directory.");
        return destination;
    }
}
