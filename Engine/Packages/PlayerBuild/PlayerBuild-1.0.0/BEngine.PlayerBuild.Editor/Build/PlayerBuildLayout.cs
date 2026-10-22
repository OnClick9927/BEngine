using System.Text;
using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

internal sealed class PlayerBuildLayout
{
    internal const string BuildDirectoryName = "Build";
    internal const string HotResourceDirectoryName = "hotres";
    internal const string StreamingAssetsDirectoryName = "StreamingAssets";

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
        DataDirectoryName = $"{playerName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}";
        DataDirectory = pathService.Combine(RootDirectory, DataDirectoryName);
        StreamingAssetsDirectory = pathService.Combine(DataDirectory, StreamingAssetsDirectoryName);
        AssemblyDirectory = pathService.Combine(
            DataDirectory, PlayerPackagedResourceAddresses.AssemblyDirectoryName);
        ResourceDirectory = DataDirectory;
        ResourcesDirectory = pathService.Combine(
            DataDirectory, PlayerPackagedResourceAddresses.ResourcesDirectoryName);
        PlayerResourceArchivePath = pathService.Combine(
            ResourcesDirectory, PlayerPackagedResourceAddresses.PlayerArchiveFileName);
        AotResourceArchivePath = pathService.Combine(ResourcesDirectory, BuiltInResourceArchive.FileName);
        PlayerResourceStagingDirectory = DataDirectory;
        RuntimeMetadataStagingPath = pathService.Combine(
            PlayerResourceStagingDirectory, RuntimeMetadataSerializer.FileName);
        BuildTargetManifestStagingPath = pathService.Combine(
            PlayerResourceStagingDirectory, BuildTargetManifest.FileName);
        SplashImageStagingPath = pathService.Combine(PlayerResourceStagingDirectory, "splash.png");
    }

    internal string RootDirectory { get; }
    internal string PlayerName { get; }
    internal string DataDirectoryName { get; }
    internal string DataDirectory { get; }
    internal string StreamingAssetsDirectory { get; }
    internal string AssemblyDirectory { get; }
    internal string ResourceDirectory { get; }
    internal string ResourcesDirectory { get; }
    internal string PlayerResourceArchivePath { get; }
    internal string AotResourceArchivePath { get; }
    internal string PlayerResourceStagingDirectory { get; }
    internal string RuntimeMetadataStagingPath { get; }
    internal string BuildTargetManifestStagingPath { get; }
    internal string SplashImageStagingPath { get; }

    internal string GetExecutablePath(BuildTargetPlatform platform) => _pathService.Combine(RootDirectory,
        platform == BuildTargetPlatform.Windows ? $"{PlayerName}.exe" : PlayerName);

    internal static PlayerBuildLayout Create(ProjectWorkspace workspace, string rootDirectory)
        => Create(workspace, rootDirectory,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static PlayerBuildLayout Create(
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

    internal static string GetDefaultBuildRoot(string projectPath)
        => GetDefaultBuildRoot(projectPath, EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string GetDefaultBuildRoot(string projectPath, PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(pathService);
        var projectDirectory = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(projectPath));
        return pathService.Combine(projectDirectory, BuildDirectoryName);
    }

    internal static void ValidateOutputLocation(
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

    internal static string GetDefaultHotResourceRoot(string projectPath) =>
        GetDefaultHotResourceRoot(projectPath, EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string GetDefaultHotResourceRoot(string projectPath, PathUtility pathService) =>
        pathService.Combine(GetDefaultBuildRoot(projectPath, pathService), HotResourceDirectoryName);

    internal void CreateDataDirectories()
    {
        _fileSystem.CreateDirectory(DataDirectory);
        _fileSystem.CreateDirectory(AssemblyDirectory);
        var assemblyDirectoryMarker = _pathService.Combine(
            AssemblyDirectory, PlayerPackagedResourceAddresses.AssemblyDirectoryMarkerFileName);
        if (!_fileSystem.FileExists(assemblyDirectoryMarker))
            _fileSystem.WriteAllText(assemblyDirectoryMarker,
                "BEngine packaged assembly directory. Runtime loaders ignore non-DLL files.\n");
        _fileSystem.CreateDirectory(ResourcesDirectory);
    }

    internal static string SanitizePlayerName(string value)
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
        var reserved = normalized.Split('.')[0];
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

    internal static void ValidateLowercaseOutputTree(string rootDirectory)
        => ValidateLowercaseOutputTree(
            rootDirectory,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static void ValidateLowercaseOutputTree(
        string rootDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var root = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(rootDirectory));
        if (!fileSystem.DirectoryExists(root))
            throw new DirectoryNotFoundException($"Build output was not found: '{root}'.");
        ValidateDirectory(root, root, fileSystem, pathService);
    }

    private static void ValidateDirectory(
        string root,
        string directory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        foreach (var path in fileSystem.EnumerateFileSystemEntries(directory))
        {
            var name = pathService.GetFileName(path);
            // StreamingAssets is a public raw-file boundary: its canonical folder and
            // project-owned payload retain their original case on every platform.
            if (name.Equals(StreamingAssetsDirectoryName, StringComparison.Ordinal) &&
                (directory.Equals(root, StringComparison.Ordinal) ||
                 pathService.GetDirectoryName(directory)?.Equals(root, StringComparison.Ordinal) == true) &&
                pathService.GetFileName(directory).EndsWith(
                    PlayerPackagedResourceAddresses.DataDirectorySuffix, StringComparison.Ordinal) &&
                fileSystem.DirectoryExists(path) &&
                (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                continue;
            if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Packaged file and directory names must be lowercase: " +
                    $"'{pathService.GetRelativePath(root, path)}'.");
            if (fileSystem.DirectoryExists(path))
                ValidateDirectory(root, path, fileSystem, pathService);
        }
    }
}
