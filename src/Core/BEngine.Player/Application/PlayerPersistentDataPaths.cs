using System.Security.Cryptography;
using System.Text;
using BEngine.Build;
using BEngine.ProjectSystem;

namespace BEngine.Player;

public static class PlayerPersistentDataPaths
{
    private const int MaximumDirectorySegmentLength = 80;

    public static string ResolveDefault(string companyName, string productName)
    {
        var platformRoot = ResolvePlatformRoot();
        var company = SanitizeDirectorySegment(companyName, "DefaultCompany");
        var product = SanitizeDirectorySegment(productName, "BEngine Game");
        var result = Path.GetFullPath(Path.Combine(platformRoot, company, product));
        EnsureDescendant(platformRoot, result);
        return result;
    }

    internal static string Resolve(
        string? explicitOverride,
        ProjectWorkspace workspace,
        string companyName,
        string productName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return ResolveExplicitOverride(explicitOverride);
        return workspace.RuntimeMetadata?.PlayerBootstrap is { } manifest
            ? ResolvePackaged(workspace.RootPath, manifest)
            : ResolveDefault(companyName, productName);
    }

    public static string ResolveExplicitOverride(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException(
                "The Player cache override must be an absolute path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string ResolvePackaged(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest)
    {
        var playerRoot = ResolvePlayerRoot(runtimeResourceDirectory, manifest);
        var cacheRoot = manifest.ResolveCacheDirectory(playerRoot);
        return TryMigrateLegacyDefaultCache(playerRoot, manifest, cacheRoot);
    }

    private static string TryMigrateLegacyDefaultCache(
        string playerRoot,
        PlayerBootstrapManifest manifest,
        string cacheRoot)
    {
        if (!PlayerBootstrapManifest.NormalizeCacheDirectory(manifest.CacheDirectory)
                .Equals(PlayerBootstrapManifest.DefaultCacheDirectory, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(cacheRoot))
            return cacheRoot;

        var legacyRoot = Path.Combine(playerRoot, PlayerBootstrapManifest.LegacyCacheDirectory);
        if (!Directory.Exists(legacyRoot)) return cacheRoot;
        var legacyInfo = new DirectoryInfo(legacyRoot);
        legacyInfo.Refresh();
        if (legacyInfo.LinkTarget is not null ||
            (legacyInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            return cacheRoot;

        try
        {
            Directory.Move(legacyRoot, cacheRoot);
            return cacheRoot;
        }
        catch (IOException)
        {
            return legacyRoot;
        }
        catch (UnauthorizedAccessException)
        {
            return legacyRoot;
        }
    }

    internal static string ResolvePlayerRoot(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeResourceDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();

        var runtimeRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(runtimeResourceDirectory));
        var playerRoot = runtimeRoot;
        var resourceSegments = manifest.ResourceDirectory.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var _ in resourceSegments)
            playerRoot = Directory.GetParent(playerRoot)?.FullName ??
                         throw new InvalidDataException(
                             "Player runtime metadata cannot be mapped to its package root.");

        var expectedRuntimeRoot = Path.TrimEndingDirectorySeparator(
            manifest.ResolveResourceDirectory(playerRoot));
        if (!PathsEqual(expectedRuntimeRoot, runtimeRoot))
            throw new InvalidDataException(
                "Player runtime metadata is not stored at its declared ResourceDirectory.");
        return playerRoot;
    }

    private static string ResolvePlatformRoot()
    {
        if (OperatingSystem.IsBrowser()) return "/bengine-persistent-data";

        string root;
        if (OperatingSystem.IsMacOS())
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = string.IsNullOrWhiteSpace(profile)
                ? string.Empty
                : Path.Combine(profile, "Library", "Application Support");
        }
        else if (OperatingSystem.IsLinux())
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = !string.IsNullOrWhiteSpace(xdgDataHome) && Path.IsPathRooted(xdgDataHome)
                ? xdgDataHome
                : string.IsNullOrWhiteSpace(profile)
                    ? string.Empty
                    : Path.Combine(profile, ".local", "share");
        }
        else
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                "The operating system did not provide an application persistent-data directory.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    private static string SanitizeDirectorySegment(string value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Normalize().Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        invalid.UnionWith(['<', '>', ':', '"', '/', '\\', '|', '?', '*']);
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            builder.Append(char.IsControl(character) || invalid.Contains(character) ? '_' : character);

        var result = builder.ToString().Trim(' ', '.');
        if (result.Length == 0 || result is "." or "..") result = fallback;
        if (IsWindowsReservedName(result)) result += "_";
        if (result.Length <= MaximumDirectorySegmentLength) return result;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)))
            .ToLowerInvariant()[..12];
        return $"{result[..(MaximumDirectorySegmentLength - hash.Length - 1)].TrimEnd(' ', '.')}-{hash}";
    }

    private static bool IsWindowsReservedName(string value)
    {
        var stem = value.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
        return stem.Length == 4 && char.IsAsciiDigit(stem[3]) && stem[3] != '0' &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureDescendant(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Player persistent-data path '{path}' escapes platform root '{root}'.");
    }

    private static bool PathsEqual(string left, string right) =>
        left.Equals(right, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
}
