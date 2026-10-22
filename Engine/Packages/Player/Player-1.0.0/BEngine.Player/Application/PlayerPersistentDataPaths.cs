using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Player;

public static class PlayerPersistentDataPaths
{
    public static string ResolveDefault(string companyName, string productName)
        => ResolveDefault(companyName, productName, CaptureServices());

    private static string ResolveDefault(
        string companyName,
        string productName,
        PlayerPathServices services)
    {
        return services.Paths.GetFullPath(
            services.ApplicationPaths.GetPersistentDataDirectory(companyName, productName));
    }

    internal static string Resolve(
        string? explicitOverride,
        ProjectWorkspace workspace,
        string companyName,
        string productName,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var services = CaptureServices(
            fileSystem, pathService, platformService, applicationPaths);
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return ResolveExplicitOverride(explicitOverride, services.Paths);
        // The platform service also backs Application.persistentDataPath. Packaged
        // content never changes the writable data root to a directory beside the executable.
        return ResolveDefault(companyName, productName, services);
    }

    public static string ResolveExplicitOverride(string path)
        => ResolveExplicitOverride(path, RuntimeServiceAccess.PathService);

    internal static string ResolveExplicitOverride(string path, PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!pathService.IsPathFullyQualified(path))
            throw new InvalidDataException(
                "The Player cache override must be an absolute path.");
        return pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(path));
    }

    public static string ResolvePackaged(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest)
        => ResolvePackaged(runtimeResourceDirectory, manifest, CaptureServices());

    internal static string ResolvePackaged(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths) => ResolvePackaged(
        runtimeResourceDirectory,
        manifest,
        CaptureServices(fileSystem, pathService, platformService, applicationPaths));

    private static string ResolvePackaged(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest,
        PlayerPathServices services)
    {
        // Keep the package-location validation, then use its authored project identity
        // so early diagnostics and the running application select the same data path.
        ResolvePlayerRoot(runtimeResourceDirectory, manifest, services);
        var workspace = PlayerWorkspace.OpenRuntime(
            runtimeResourceDirectory, services.FileSystem, services.Paths);
        var settings = workspace.SettingsOverride ?? new ProjectSettingsData();
        return ResolveDefault(settings.CompanyName, settings.ProductName, services);
    }

    internal static void EnsurePhysicalDirectoryTree(
        string path,
        string description,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null)
    {
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        if (!fileSystem.DirectoryExists(path)) return;
        var pending = new Stack<string>();
        pending.Push(pathService.GetFullPath(path));
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            EnsureNotReparsePoint(directory, description, fileSystem);
            foreach (var entry in fileSystem.EnumerateFileSystemEntries(directory))
            {
                EnsureNotReparsePoint(entry, description, fileSystem);
                if (fileSystem.DirectoryExists(entry)) pending.Push(entry);
            }
        }
    }

    private static void EnsureNotReparsePoint(
        string path,
        string description,
        FileUtility fileSystem)
    {
        if ((fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"The {description} cannot contain a reparse point: '{path}'.");
    }

    internal static string ResolvePlayerRoot(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
        => ResolvePlayerRoot(
            runtimeResourceDirectory,
            manifest,
            CaptureServices(fileSystem, pathService, platformService, applicationPaths));

    private static string ResolvePlayerRoot(
        string runtimeResourceDirectory,
        PlayerBootstrapManifest manifest,
        PlayerPathServices services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeResourceDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();

        var runtimeRoot = services.Paths.TrimEndingDirectorySeparator(
            services.Paths.GetFullPath(runtimeResourceDirectory));
        var playerRoot = runtimeRoot;
        var resourceSegments = manifest.ResourceDirectory.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var _ in resourceSegments)
            playerRoot = services.Paths.GetDirectoryName(
                             services.Paths.TrimEndingDirectorySeparator(playerRoot)) ??
                          throw new InvalidDataException(
                             "Player runtime metadata cannot be mapped to its package root.");

        var expectedRuntimeRoot = services.Paths.TrimEndingDirectorySeparator(
            manifest.ResolveResourceDirectory(playerRoot));
        if (!PathsEqual(expectedRuntimeRoot, runtimeRoot, services.Platform))
            throw new InvalidDataException(
                "Player runtime metadata is not stored at its declared ResourceDirectory.");
        return playerRoot;
    }

    private static bool PathsEqual(
        string left,
        string right,
        PlatformUtility platformService) =>
        left.Equals(right, platformService.IsWindows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private static PlayerPathServices CaptureServices(
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null) => new(
        fileSystem ?? RuntimeServiceAccess.FileSystem,
        pathService ?? RuntimeServiceAccess.PathService,
        platformService ?? RuntimeServiceAccess.PlatformService,
        applicationPaths ?? RuntimeServiceAccess.ApplicationPaths);

    private readonly record struct PlayerPathServices(
        FileUtility FileSystem,
        PathUtility Paths,
        PlatformUtility Platform,
        ApplicationPathUtility ApplicationPaths);
}
