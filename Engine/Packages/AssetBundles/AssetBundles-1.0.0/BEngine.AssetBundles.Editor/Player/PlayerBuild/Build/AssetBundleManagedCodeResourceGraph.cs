using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor.Build;

/// <summary>Adapts the content partition's roots and archive dependencies to the code build.</summary>
internal sealed class AssetBundleManagedCodeResourceGraph(
    ProjectWorkspace workspace, IProjectAssetDatabase database, IReadOnlyList<string> roots,
    IReadOnlyList<string> candidates, IReadOnlySet<string> resources,
    FileUtility files, PathUtility paths) : IPlayerManagedCodeResourceGraph
{
    public IReadOnlyList<string> CollectAssets(IReadOnlyList<string> assemblyPaths,
        IReadOnlyCollection<string> resourceFiles, PlayerStrippingSettings settings, CancellationToken cancellationToken)
    {
        var selected = PlayerResourceStripper.GetProjectRoots(roots, candidates, assemblyPaths,
            settings, database.assets, resourceFiles);
        var assets = AssetBundleBuilder.CollectDependencies(workspace, database, selected,
            files, paths, includeImplicitRuntimeRoots: false, cancellationToken);
        PlayerProjectAssetPartition.ValidateAotBoundary(assets, resources);
        return assets;
    }

    public IReadOnlyList<RuntimePackageResourceInput> FilterResources(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies, IReadOnlyList<RuntimePackageResourceInput> availableResources,
        IReadOnlyList<string> referencedFiles, PlayerStrippingSettings settings, PlayerStrippingReport report) =>
        PlayerResourceStripper.FilterPackageResources(new(assemblies, availableResources, []),
            referencedFiles, settings, report).PackageResources;
}
