using BEngine.Hosting;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Host resource reachability, independent of the content archive or resource format.</summary>
public static class PlayerResourceBuildPipeline
{
    public static IReadOnlyList<RuntimePackageResourceInput> SelectHostResources(PlayerBuildContext context,
        IReadOnlyList<RuntimePackageResourceInput> resources,
        Func<RuntimePackageResourceInput, bool>? requiredRoot = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.StrippingReport is not { } report) return resources;
        var workspace = ProjectWorkspace.Open(context.Request.ProjectPath, FileUtility.Current, PathUtility.Current);
        var settings = PlayerStrippingSettings.Load(workspace);
        var references = PlayerResourceReferenceSet.Read(context.RuntimeManagedCodeAssemblyPaths
            .Concat(PlayerSerializationRoots.HostAssemblyPaths), cancellationToken: cancellationToken);
        return references.SelectResources(resources, settings.PreservedAssets.Where(static path =>
            path.StartsWith(HostPackagedResourceAddresses.CoreResourcesPrefix, StringComparison.OrdinalIgnoreCase)),
            report, requiredRoot, cancellationToken);
    }
}
