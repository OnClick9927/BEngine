using YamlDotNet.Serialization;
using BEngine.Content;
using BEngine.Platform;

namespace BEngine.Build;

public sealed class PlayerBootstrapManifest
{
    public const string CurrentFormat = "BEngine.Player";
    public const int CurrentVersion = 4;
    public const string FileName = "bengine.player";
    public const string DefaultCacheDirectory = "sandbox";
    public const string LegacyCacheDirectory = "snadbox";

    private static FileUtility FileSystem => RuntimeServiceAccess.FileSystem;
    private static PathUtility Paths => RuntimeServiceAccess.PathService;

    [YamlMember(Order = 0)] public string Format { get; set; } = CurrentFormat;
    [YamlMember(Order = 1)] public int Version { get; set; } = CurrentVersion;
    [YamlMember(Order = 2)] public string ProductName { get; set; } = string.Empty;
    [YamlMember(Order = 3)] public string BuildVersion { get; set; } = "1.0.0";
    [YamlMember(Order = 4)] public bool DevelopmentBuild { get; set; }
    [YamlMember(Order = 5)] public bool WritePlayerLog { get; set; } = true;
    [YamlMember(Order = 6)] public string Executable { get; set; } = string.Empty;
    [YamlMember(Order = 7)] public string DataDirectory { get; set; } = string.Empty;
    [YamlMember(Order = 8)] public string ResourceDirectory { get; set; } = string.Empty;
    [YamlMember(Order = 9)] public string AssemblyDirectory { get; set; } = string.Empty;
    [YamlMember(Order = 10)] public string BuildTargetManifest { get; set; } = string.Empty;
    [YamlMember(Order = 11)] public bool SplashScreenEnabled { get; set; } = true;
    [YamlMember(Order = 12)] public string SplashImage { get; set; } = string.Empty;
    [YamlMember(Order = 13)] public string SplashBackgroundColor { get; set; } = "#10161A";
    [YamlMember(Order = 14)] public float SplashMinimumDurationSeconds { get; set; } = 1.5f;
    [YamlMember(Order = 15)] public string CacheDirectory { get; set; } = DefaultCacheDirectory;
    [YamlMember(Order = 16)] public string HotUpdateStartupScene { get; set; } = string.Empty;
    [YamlMember(Order = 17)] public string PlayerResourceArchive { get; set; } = string.Empty;
    [YamlMember(Order = 18)] public string BuildTargetResource { get; set; } = string.Empty;
    [YamlMember(Order = 19)] public string SplashImageResource { get; set; } = string.Empty;

    public void Validate()
    {
        if (!Format.Equals(CurrentFormat, StringComparison.Ordinal) || Version is < 1 or > CurrentVersion)
            throw new InvalidDataException($"Unsupported Player bootstrap format '{Format}' version {Version}.");
        ArgumentException.ThrowIfNullOrWhiteSpace(ProductName);
        if (string.IsNullOrWhiteSpace(BuildVersion) || BuildVersion.Length > 64 ||
            BuildVersion.Any(character => char.IsControl(character)))
            throw new InvalidDataException("Player bootstrap BuildVersion is invalid.");
        ValidateRelativeFileName(Executable, nameof(Executable));
        ValidateRelativePath(DataDirectory, nameof(DataDirectory));
        ValidateRelativePath(ResourceDirectory, nameof(ResourceDirectory));
        ValidateRelativePath(AssemblyDirectory, nameof(AssemblyDirectory));
        ValidateRelativePath(CacheDirectory, nameof(CacheDirectory));
        ValidateOptionalScenePath(HotUpdateStartupScene, nameof(HotUpdateStartupScene));

        if (Version >= 4)
        {
            ValidateArchiveContract();
        }
        else
        {
            ValidateLegacyResourceContract();
        }
        if (!TryParseHtmlColor(SplashBackgroundColor))
            throw new InvalidDataException(
                "Player bootstrap SplashBackgroundColor must use #RRGGBB or #RRGGBBAA format.");
        if (!float.IsFinite(SplashMinimumDurationSeconds) ||
            SplashMinimumDurationSeconds is < 0 or > 30)
            throw new InvalidDataException(
                "Player bootstrap SplashMinimumDurationSeconds must be between 0 and 30 seconds.");
        if (!IsWithin(DataDirectory, AssemblyDirectory))
            throw new InvalidDataException("Player bootstrap AssemblyDirectory must be inside DataDirectory.");
        if (PathsOverlap(CacheDirectory, Executable) || PathsOverlap(CacheDirectory, DataDirectory))
            throw new InvalidDataException(
                "Player bootstrap CacheDirectory must not overlap the executable or DataDirectory.");
    }

    public string ResolveResourceDirectory(string playerDirectory) =>
        ResolveInside(playerDirectory, ResourceDirectory);

    public string ResolveDataDirectory(string playerDirectory) =>
        ResolveInside(playerDirectory, DataDirectory);

    public string ResolveAssemblyDirectory(string playerDirectory) =>
        ResolveInside(playerDirectory, AssemblyDirectory);

    public string ResolveExecutable(string playerDirectory) =>
        ResolveInside(playerDirectory, Executable);

    public string ResolveBuildTargetManifest(string playerDirectory) =>
        Version >= 4
            ? throw new InvalidOperationException(
                "Archived Player bootstrap metadata has no loose build-target manifest path.")
            : ResolveInside(playerDirectory, BuildTargetManifest);

    public string? ResolveSplashImage(string playerDirectory) =>
        Version >= 4 || string.IsNullOrWhiteSpace(SplashImage)
            ? null
            : ResolveInside(playerDirectory, SplashImage);

    public string ResolvePlayerResourceArchive(string playerDirectory)
    {
        if (Version < 4)
            throw new InvalidOperationException(
                "Legacy Player bootstrap metadata has no packaged resource archive path.");
        return ResolveInside(playerDirectory, PlayerResourceArchive);
    }

    public string ResolveCacheDirectory(string playerDirectory)
    {
        var cacheDirectory = NormalizeCacheDirectory(CacheDirectory);
        if ((!string.IsNullOrWhiteSpace(Executable) && PathsOverlap(cacheDirectory, Executable)) ||
            (!string.IsNullOrWhiteSpace(DataDirectory) && PathsOverlap(cacheDirectory, DataDirectory)))
            throw new InvalidDataException(
                "Player bootstrap CacheDirectory must not overlap the executable or DataDirectory.");
        var resolved = ResolveInside(playerDirectory, cacheDirectory);
        EnsureNoReparsePointTraversal(playerDirectory, resolved, nameof(CacheDirectory));
        return resolved;
    }

    public static string NormalizeCacheDirectory(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? DefaultCacheDirectory
            : value.Trim().Replace('\\', '/');
        if (normalized.Equals(LegacyCacheDirectory, StringComparison.OrdinalIgnoreCase))
            normalized = DefaultCacheDirectory;
        ValidateRelativePath(normalized, nameof(CacheDirectory));
        return string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private void ValidateArchiveContract()
    {
        if (!PathsEqual(DataDirectory, ResourceDirectory))
            throw new InvalidDataException(
                "Archived Player bootstrap ResourceDirectory must equal DataDirectory.");
        ValidateRelativePath(PlayerResourceArchive, nameof(PlayerResourceArchive));
        var expectedArchive = $"{DataDirectory.Replace('\\', '/').TrimEnd('/')}/" +
                              $"{PlayerPackagedResourceAddresses.ResourcesDirectoryName}/" +
                              PlayerPackagedResourceAddresses.PlayerArchiveFileName;
        if (!PathsEqual(expectedArchive, PlayerResourceArchive))
            throw new InvalidDataException(
                $"Player bootstrap archive must use the conventional path '{expectedArchive}'.");
        if (!string.IsNullOrWhiteSpace(BuildTargetManifest) || !string.IsNullOrWhiteSpace(SplashImage))
            throw new InvalidDataException(
                "Archived Player bootstrap metadata cannot declare legacy loose resource paths.");
        if (!BuildTargetResource.Equals(
                PlayerPackagedResourceAddresses.BuildTargetManifest, StringComparison.Ordinal))
            throw new InvalidDataException("Player bootstrap BuildTargetResource is invalid.");
        if (SplashScreenEnabled)
        {
            if (!SplashImageResource.Equals(
                    PlayerPackagedResourceAddresses.SplashImage, StringComparison.Ordinal))
                throw new InvalidDataException("Player bootstrap SplashImageResource is invalid.");
        }
        else if (!string.IsNullOrWhiteSpace(SplashImageResource))
            throw new InvalidDataException(
                "Player bootstrap SplashImageResource must be empty when the splash screen is disabled.");
    }

    private void ValidateLegacyResourceContract()
    {
        if (!string.IsNullOrWhiteSpace(PlayerResourceArchive) ||
            !string.IsNullOrWhiteSpace(BuildTargetResource) ||
            !string.IsNullOrWhiteSpace(SplashImageResource))
            throw new InvalidDataException(
                "Legacy Player bootstrap metadata cannot declare archived resource fields.");
        ValidateRelativePath(BuildTargetManifest, nameof(BuildTargetManifest));
        if (SplashScreenEnabled)
        {
            if (Version >= 2) ValidateRelativePath(SplashImage, nameof(SplashImage));
            else if (!string.IsNullOrWhiteSpace(SplashImage))
                ValidateRelativePath(SplashImage, nameof(SplashImage));
            if (!string.IsNullOrWhiteSpace(SplashImage) &&
                !Paths.GetExtension(SplashImage).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Player bootstrap SplashImage must be a PNG file.");
        }
        if (!IsWithin(DataDirectory, ResourceDirectory))
            throw new InvalidDataException("Player bootstrap ResourceDirectory must be inside DataDirectory.");
        if (!IsWithin(ResourceDirectory, BuildTargetManifest))
            throw new InvalidDataException("Player bootstrap BuildTargetManifest must be inside ResourceDirectory.");
        if (!string.IsNullOrWhiteSpace(SplashImage) && !IsWithin(ResourceDirectory, SplashImage))
            throw new InvalidDataException("Player bootstrap SplashImage must be inside ResourceDirectory.");
    }

    private static void ValidateRelativeFileName(string value, string propertyName)
    {
        ValidateRelativePath(value, propertyName);
        if (value.Contains('/') || value.Contains('\\'))
            throw new InvalidDataException($"Player bootstrap {propertyName} must be a file name.");
    }

    private static void ValidateRelativePath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Player bootstrap {propertyName} must be a relative path.");
        var normalized = value.Replace('\\', '/');
        if (normalized.Length > 1024 || Paths.IsPathRooted(value) || normalized.StartsWith('/') ||
            normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            throw new InvalidDataException($"Player bootstrap {propertyName} must be a relative path.");
        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"Player bootstrap {propertyName} cannot traverse directories.");
        foreach (var segment in segments)
        {
            if (segment.Length > 255 || segment != segment.Trim() || segment.EndsWith('.') ||
                IsWindowsReservedName(segment) ||
                segment.Any(character => char.IsControl(character) ||
                    character is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
                throw new InvalidDataException(
                    $"Player bootstrap {propertyName} contains an invalid directory segment.");
        }
    }

    private static void ValidateOptionalScenePath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        ValidateRelativePath(value, propertyName);
        var normalized = value.Replace('\\', '/');
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !normalized.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Player bootstrap {propertyName} must be an Assets/*.scene.yaml path.");
    }

    private static string ResolveInside(string playerDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        var paths = Paths;
        var root = paths.TrimEndingDirectorySeparator(paths.GetFullPath(playerDirectory));
        var resolved = paths.GetFullPath(paths.Combine(root,
            relativePath.Replace('\\', paths.DirectorySeparatorChar)
                .Replace('/', paths.DirectorySeparatorChar)));
        var relative = paths.GetRelativePath(root, resolved);
        if (paths.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{paths.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException($"Player bootstrap path escapes '{root}': '{relativePath}'.");
        return resolved;
    }

    private static void EnsureNoReparsePointTraversal(
        string playerDirectory,
        string resolved,
        string propertyName)
    {
        var paths = Paths;
        var fileSystem = FileSystem;
        var root = paths.TrimEndingDirectorySeparator(paths.GetFullPath(playerDirectory));
        EnsurePhysicalDirectory(root, propertyName);
        var current = root;
        foreach (var segment in paths.GetRelativePath(root, resolved)
                     .Split(paths.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = paths.Combine(current, segment);
            if (!fileSystem.DirectoryExists(current) && !fileSystem.FileExists(current)) continue;
            EnsurePhysicalDirectory(current, propertyName);
        }
    }

    private static void EnsurePhysicalDirectory(string path, string propertyName)
    {
        var fileSystem = FileSystem;
        var isFile = fileSystem.FileExists(path);
        var isDirectory = fileSystem.DirectoryExists(path);
        if (isFile && !isDirectory)
            throw new InvalidDataException(
                $"Player bootstrap {propertyName} traverses a file: '{path}'.");
        if ((isFile || isDirectory) &&
            (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"Player bootstrap {propertyName} cannot traverse a reparse point: '{path}'.");
    }

    private static bool IsWindowsReservedName(string value)
    {
        var stem = value.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
        return stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWithin(string parent, string child)
    {
        var normalizedParent = parent.Replace('\\', '/').TrimEnd('/');
        var normalizedChild = child.Replace('\\', '/');
        return normalizedChild.StartsWith(normalizedParent + '/', StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsOverlap(string left, string right) =>
        PathsEqual(left, right) || IsWithin(left, right) || IsWithin(right, left);

    private static bool PathsEqual(string left, string right) =>
        left.Replace('\\', '/').TrimEnd('/').Equals(
            right.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool TryParseHtmlColor(string value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#') return false;
        return value.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0 ||
               value.AsSpan(1).ToString().All(static character =>
                   char.IsAsciiHexDigit(character));
    }
}
