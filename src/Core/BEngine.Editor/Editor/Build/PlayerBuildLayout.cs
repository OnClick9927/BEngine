using System.Text;
using BEngine.Build;
using BEngine.Content;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

internal sealed class PlayerBuildLayout
{
    internal const string BuildDirectoryName = "Build";
    internal const string HotResourceDirectoryName = "hotres";

    private PlayerBuildLayout(string rootDirectory, string playerName)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        PlayerName = playerName;
        DataDirectoryName = $"{playerName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}";
        DataDirectory = Path.Combine(RootDirectory, DataDirectoryName);
        AssemblyDirectory = Path.Combine(
            DataDirectory, PlayerPackagedResourceAddresses.AssemblyDirectoryName);
        ResourceDirectory = DataDirectory;
        ResourcesDirectory = Path.Combine(
            DataDirectory, PlayerPackagedResourceAddresses.ResourcesDirectoryName);
        PlayerResourceArchivePath = Path.Combine(
            ResourcesDirectory, PlayerPackagedResourceAddresses.PlayerArchiveFileName);
        AotResourceArchivePath = Path.Combine(ResourcesDirectory, BuiltInResourceArchive.FileName);
        PlayerResourceStagingDirectory = DataDirectory;
        RuntimeMetadataStagingPath = Path.Combine(
            PlayerResourceStagingDirectory, RuntimeMetadataSerializer.FileName);
        BuildTargetManifestStagingPath = Path.Combine(
            PlayerResourceStagingDirectory, BuildTargetManifest.FileName);
        SplashImageStagingPath = Path.Combine(PlayerResourceStagingDirectory, "splash.png");
    }

    internal string RootDirectory { get; }
    internal string PlayerName { get; }
    internal string DataDirectoryName { get; }
    internal string DataDirectory { get; }
    internal string AssemblyDirectory { get; }
    internal string ResourceDirectory { get; }
    internal string ResourcesDirectory { get; }
    internal string PlayerResourceArchivePath { get; }
    internal string AotResourceArchivePath { get; }
    internal string PlayerResourceStagingDirectory { get; }
    internal string RuntimeMetadataStagingPath { get; }
    internal string BuildTargetManifestStagingPath { get; }
    internal string SplashImageStagingPath { get; }

    internal string GetExecutablePath(BuildTargetPlatform platform) => Path.Combine(RootDirectory,
        platform == BuildTargetPlatform.Windows ? $"{PlayerName}.exe" : PlayerName);

    internal static PlayerBuildLayout Create(ProjectWorkspace workspace, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return new PlayerBuildLayout(rootDirectory, SanitizePlayerName(workspace.Project.Name));
    }

    internal static string GetDefaultBuildRoot(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        return Path.Combine(Path.GetFullPath(projectPath), BuildDirectoryName);
    }

    internal static string GetDefaultHotResourceRoot(string projectPath) =>
        Path.Combine(GetDefaultBuildRoot(projectPath), HotResourceDirectoryName);

    internal void CreateDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AssemblyDirectory);
        Directory.CreateDirectory(ResourcesDirectory);
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Build output was not found: '{root}'.");
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Packaged file and directory names must be lowercase: " +
                    $"'{Path.GetRelativePath(root, path)}'.");
        }
    }
}
