using BEngine.Hosting;
using BEngine.ProjectSystem;
using BEngine.Player.ProjectSystem;
using BEngine.Content;
using BEngine.Player.Content;
using BEngine.Platform;

namespace BEngine.Player;

internal static class PlayerAssetEnvironment
{
    internal static void Initialize(
        RuntimeWorkspace workspace,
        PlayerBuiltInResourceProvider? packagedResources = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        fileSystem ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
        pathService ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
        platformService ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>();
        applicationPaths ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.ApplicationPathUtility>();
        PlayerHostServices.ConfigureDataDirectory(workspace.GetRuntimeMetadata()?.PlayerBootstrap is { } manifest
            ? manifest.ResolveDataDirectory(PlayerPersistentDataPaths.ResolvePlayerRoot(
                workspace.RootPath, manifest, fileSystem, pathService, platformService, applicationPaths))
            : pathService.GetFullPath(workspace.AssetsPath));
        if (packagedResources is null)
            RegisterCoreResourceRoot(
                workspace, fileSystem, pathService, platformService, applicationPaths);
        else ActivatePackagedCoreResources(packagedResources);
        BAsset.ClearLoadedAssets();
    }

    private static void ActivatePackagedCoreResources(PlayerBuiltInResourceProvider resources) => resources.Activate();

    private static void RegisterCoreResourceRoot(RuntimeWorkspace workspace, FileUtility files,
        PathUtility paths, PlatformUtility platform, ApplicationPathUtility applicationPaths)
    {
        foreach (var root in new[] { workspace.RootPath, applicationPaths.BaseDirectory })
            if (files.DirectoryExists(paths.Combine(root, "Resources"))) Resources.RegisterResourceRoot(root);

    }
}
