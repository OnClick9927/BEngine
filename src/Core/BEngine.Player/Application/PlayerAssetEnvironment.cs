using BEngine.ProjectSystem;
using BEngine.Content;

namespace BEngine.Player;

internal static class PlayerAssetEnvironment
{
    internal static void Initialize(
        ProjectWorkspace workspace,
        PlayerBuiltInResourceProvider? packagedResources = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Application.dataPath = Path.GetFullPath(workspace.AssetsPath);
        if (packagedResources is null) RegisterCoreResourceRoot(workspace);
        else ActivatePackagedCoreResources(packagedResources);
        BAsset.ClearLoadedAssets();
        TextureAtlasResolver.Clear();
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

    private static void RegisterCoreResourceRoot(ProjectWorkspace workspace)
    {
        foreach (var candidate in EnumerateCoreResourceRoots(workspace))
        {
            if (!File.Exists(Path.Combine(candidate, "Resources", "Icons", "BEngine.64.rgba")) ||
                !Directory.Exists(Path.Combine(candidate, "Resources", "Shaders", "PortableScene"))) continue;
            Resources.RegisterResourceRoot(candidate);
            return;
        }
        throw new DirectoryNotFoundException(
            "BEngine runtime Resources are missing. Rebuild the Player resource archive.");
    }

    private static IEnumerable<string> EnumerateCoreResourceRoots(ProjectWorkspace workspace)
    {
        yield return workspace.RootPath;
        yield return AppContext.BaseDirectory;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
             directory = directory.Parent)
            yield return Path.Combine(directory.FullName, "src", "Core");
    }
}
