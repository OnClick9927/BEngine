using BEngine.ProjectSystem;
using BEngine.Content;
using BEngine.Platform;

namespace BEngine.Player;

internal static class PlayerAssetEnvironment
{
    internal static void Initialize(
        ProjectWorkspace workspace,
        PlayerBuiltInResourceProvider? packagedResources = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        platformService ??= RuntimeServiceAccess.PlatformService;
        applicationPaths ??= RuntimeServiceAccess.ApplicationPaths;
        Application.dataPath = workspace.GetRuntimeMetadata()?.PlayerBootstrap is { } manifest
            ? manifest.ResolveDataDirectory(PlayerPersistentDataPaths.ResolvePlayerRoot(
                workspace.RootPath, manifest, fileSystem, pathService, platformService, applicationPaths))
            : pathService.GetFullPath(workspace.AssetsPath);
        if (packagedResources is null)
            RegisterCoreResourceRoot(
                workspace, fileSystem, pathService, platformService, applicationPaths);
        else ActivatePackagedCoreResources(packagedResources);
        BAsset.ClearLoadedAssets();
        SpriteSourceRegistry.Invalidate();
    }

    private static void ActivatePackagedCoreResources(PlayerBuiltInResourceProvider resources)
    {
        var icon = PlayerPackagedResourceAddresses.CoreResourcesPrefix + "Icons/BEngine.64.rgba";
        var shaders = PlayerPackagedResourceAddresses.CoreResourcesPrefix + "Shaders/PortableScene/";
        if (!resources.ContainsAddress(icon) ||
            !resources.EnumerateAddresses(shaders).Any())
            throw new InvalidDataException(
                "The packaged Player resource archive has no runtime icon or PortableScene shaders.");
        resources.Activate();
    }

    private static void RegisterCoreResourceRoot(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths)
    {
        foreach (var candidate in EnumerateCoreResourceRoots(
                     workspace, pathService, platformService, applicationPaths))
        {
            if (!fileSystem.FileExists(pathService.Combine(
                    candidate, "Resources", "Icons", "BEngine.64.rgba")) ||
                !fileSystem.DirectoryExists(pathService.Combine(
                    candidate, "Resources", "Shaders", "PortableScene"))) continue;
            Resources.RegisterResourceRoot(candidate);
            return;
        }
        throw new DirectoryNotFoundException(
            "BEngine runtime Resources are missing. Rebuild the Player resource archive.");
    }

    private static IEnumerable<string> EnumerateCoreResourceRoots(
        ProjectWorkspace workspace,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths)
    {
        yield return workspace.RootPath;
        yield return applicationPaths.BaseDirectory;
        foreach (var start in new[] { applicationPaths.BaseDirectory, platformService.CurrentDirectory })
        for (string? directory = pathService.GetFullPath(start); directory is not null;
             directory = pathService.GetDirectoryName(
                 pathService.TrimEndingDirectorySeparator(directory)))
        {
            yield return pathService.Combine(directory, "src", "Core");
            yield return pathService.Combine(directory, "Engine", "src", "Core");
        }
    }
}
