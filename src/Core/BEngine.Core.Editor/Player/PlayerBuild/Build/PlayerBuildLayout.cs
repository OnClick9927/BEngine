using BEngine.Hosting;
using System.Text;
using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Engine-owned, read-only addresses of a Player staging tree. Creating a layout
/// does not publish files; the build pipeline retains output validation and transactions.</summary>
public sealed class PlayerBuildLayout
{
    internal const string BuildDirectoryName = "Build";
    internal const string HotResourceDirectoryName = "hotres";
    public const string StreamingAssetsDirectoryName = "StreamingAssets";

    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;

    private PlayerBuildLayout(
        string rootDirectory,
        string playerName,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        _fileSystem = fileSystem;
        _pathService = pathService;
        RootDirectory = pathService.GetFullPath(rootDirectory);
        PlayerName = playerName;
        DataDirectoryName = $"{playerName}{HostPackagedResourceAddresses.DataDirectorySuffix}";
        DataDirectory = pathService.Combine(RootDirectory, DataDirectoryName);
        StreamingAssetsDirectory = pathService.Combine(DataDirectory, StreamingAssetsDirectoryName);
        AssemblyDirectory = pathService.Combine(
            DataDirectory, HostPackagedResourceAddresses.NativeLibraryDirectoryName);
        ResourceDirectory = DataDirectory;
        ResourcesDirectory = pathService.Combine(
            DataDirectory, HostPackagedResourceAddresses.ResourcesDirectoryName);
        PlayerResourceArchivePath = pathService.Combine(
            ResourcesDirectory, HostPackagedResourceAddresses.PlayerArchiveFileName);
        AotResourceArchivePath = pathService.Combine(ResourcesDirectory, "aot.bresources");
        PlayerResourceStagingDirectory = DataDirectory;
        RuntimeMetadataStagingPath = pathService.Combine(
            PlayerResourceStagingDirectory, "runtime.bmeta");
        BuildTargetManifestStagingPath = pathService.Combine(
            PlayerResourceStagingDirectory, BuildTargetManifest.FileName);
        SplashImageStagingPath = pathService.Combine(PlayerResourceStagingDirectory, "splash.png");
    }

    public string RootDirectory { get; }
    public string PlayerName { get; }
    public string DataDirectoryName { get; }
    public string DataDirectory { get; }
    public string StreamingAssetsDirectory { get; }
    public string AssemblyDirectory { get; }
    public string ResourceDirectory { get; }
    public string ResourcesDirectory { get; }
    public string PlayerResourceArchivePath { get; }
    public string AotResourceArchivePath { get; }
    public string PlayerResourceStagingDirectory { get; }
    public string RuntimeMetadataStagingPath { get; }
    public string BuildTargetManifestStagingPath { get; }
    public string SplashImageStagingPath { get; }

    public string GetExecutablePath(BuildTargetPlatform platform) => _pathService.Combine(RootDirectory,
        platform == BuildTargetPlatform.Windows ? $"{PlayerName}.exe" : PlayerName);

    public static PlayerBuildLayout Create(ProjectWorkspace workspace, string rootDirectory)
        => Create(workspace, rootDirectory,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    public static PlayerBuildLayout Create(
        ProjectWorkspace workspace,
        string rootDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        return new PlayerBuildLayout(rootDirectory, SanitizePlayerName(workspace.Project.Name), fileSystem,
            pathService);
    }

    /// <summary>Returns the engine's dedicated build root without creating it.</summary>
    public static string GetDefaultBuildRoot(string projectPath)
        => GetDefaultBuildRoot(projectPath, EditorServiceAccess.GetRequiredService<PathUtility>());

    public static string GetDefaultBuildRoot(string projectPath, PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(pathService);
        var projectDirectory = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(projectPath));
        return pathService.Combine(projectDirectory, BuildDirectoryName);
    }

    /// <summary>Checks the same project-overwrite and linked-directory guards used by the
    /// build pipeline. No files are created, deleted or published by this preflight.</summary>
    public static void ValidateOutputLocation(
        string projectPath,
        string outputDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var project = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(projectPath));
        var output = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(outputDirectory));
        var buildRoot = pathService.Combine(project, BuildDirectoryName);
        if (IsSameOrChild(project, output, pathService) ||
            IsSameOrChild(output, project, pathService) && !IsSameOrChild(output, buildRoot, pathService))
            throw new InvalidOperationException(
                "Player builds inside the project are only allowed under its dedicated Build directory.");

        for (string? directory = output; directory is not null;
             directory = pathService.GetDirectoryName(directory))
        {
            if ((fileSystem.DirectoryExists(directory) || fileSystem.FileExists(directory)) &&
                (fileSystem.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    $"Player build output cannot pass through a linked directory: '{directory}'.");
        }
    }

    private static bool IsSameOrChild(string path, string root, PathUtility pathService) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.EndsWith(pathService.DirectorySeparatorChar) ||
                        root.EndsWith(pathService.AltDirectorySeparatorChar)
                ? root
                : root + pathService.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the default content-release root without creating it.</summary>
    public static string GetDefaultHotResourceRoot(string projectPath) =>
        GetDefaultHotResourceRoot(projectPath, EditorServiceAccess.GetRequiredService<PathUtility>());

    public static string GetDefaultHotResourceRoot(string projectPath, PathUtility pathService) =>
        pathService.Combine(GetDefaultBuildRoot(projectPath, pathService), HotResourceDirectoryName);

    public void CreateDataDirectories()
    {
        _fileSystem.CreateDirectory(DataDirectory);
        _fileSystem.CreateDirectory(AssemblyDirectory);
        var assemblyDirectoryMarker = _pathService.Combine(
            AssemblyDirectory, HostPackagedResourceAddresses.NativeLibraryDirectoryMarkerFileName);
        if (!_fileSystem.FileExists(assemblyDirectoryMarker))
            _fileSystem.WriteAllText(assemblyDirectoryMarker,
                "BEngine packaged assembly directory. Runtime loaders ignore non-DLL files.\n");
        _fileSystem.CreateDirectory(ResourcesDirectory);
    }

    public static string SanitizePlayerName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "benginegame" : value.Trim();
        var builder = new StringBuilder(Math.Min(source.Length, 80));
        foreach (var character in source)
        {
            if (builder.Length >= 80) break;
            builder.Append(char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.' or '(' or ')'
                ? character
                : '-');
        }
        var normalized = builder.ToString().Trim(' ', '.', '-');
        if (normalized.Length == 0) normalized = "benginegame";
        var reserved = normalized.AsSpan().Before('.');
        if (reserved.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            reserved.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            reserved.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            reserved.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            reserved.Length == 4 &&
            (reserved.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             reserved.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            reserved[3] is >= '1' and <= '9')
            normalized = $"{normalized}-game";
        return normalized.ToLowerInvariant();
    }

}
