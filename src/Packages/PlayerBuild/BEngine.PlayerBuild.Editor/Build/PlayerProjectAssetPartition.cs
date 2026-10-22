using BEngine.Platform;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

/// <summary>One project-asset boundary shared by the Player and hot-content builds.</summary>
internal static class PlayerProjectAssetPartition
{
    internal static bool IsResourcesAsset(string path)
    {
        var segments = Normalize(path).Split('/');
        return segments.Length > 2 && segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
               !segments.Skip(1).SkipLast(1).Any(static segment => segment.Equals("Editor", StringComparison.OrdinalIgnoreCase)) &&
               segments.Skip(1).SkipLast(1).Any(static segment => segment.Equals("Resources", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAotAsset(string path)
    {
        var normalized = Normalize(path).TrimEnd('/');
        return normalized.Equals(PlayerContentBuildPipeline.AotAssetRoot, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(PlayerContentBuildPipeline.AotAssetRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    internal static PlayerProjectAssetPartitionResult Collect(
        ProjectWorkspace workspace, ProjectAssetDatabase database, IReadOnlyList<string> scenes,
        FileUtility files, PathUtility paths, CancellationToken cancellationToken = default)
    {
        var runtimeAssets = AssetBundleBuilder.CollectAllAssets(database, files, paths,
            cancellationToken: cancellationToken);
        var resourceRoots = runtimeAssets.Where(IsResourcesAsset).ToArray();
        var resourceClosure = AssetBundleBuilder.CollectDependencies(workspace, database, resourceRoots,
            files, paths, includeImplicitRuntimeRoots: false, cancellationToken);
        var resourceSet = resourceClosure.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startupAssets = AssetBundleBuilder.CollectDependencies(workspace, database, scenes,
            files, paths, includeImplicitRuntimeRoots: false, cancellationToken);
        ValidateAotBoundary(startupAssets, resourceSet);
        var builtIn = startupAssets.Concat(resourceClosure).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        // The dependency closure belongs to the same package as its Resources roots,
        // even when an importer assigns one of those assets to an explicit bundle.
        var hotAssets = runtimeAssets.Where(path => !IsAotAsset(path) && !resourceSet.Contains(path)).ToArray();
        return new(resourceRoots, resourceClosure, builtIn, hotAssets);
    }

    internal static void ValidateAotBoundary(IEnumerable<string> assetPaths, IReadOnlySet<string> resourcesClosure)
    {
        var external = assetPaths.Where(path => !IsAotAsset(path) && !resourcesClosure.Contains(path)).ToArray();
        if (external.Length == 0) return;
        throw new InvalidDataException(
            $"The AOT scene may only reference project assets below '{PlayerContentBuildPipeline.AotAssetRoot}' " +
            "or main-package Resources and their dependencies. Move these assets into AOT or a Resources folder: " +
            string.Join(", ", external) + ".");
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}

internal sealed record PlayerProjectAssetPartitionResult(
    IReadOnlyList<string> ResourcesRoots,
    IReadOnlyList<string> ResourcesClosure,
    IReadOnlyList<string> BuiltInAssets,
    IReadOnlyList<string> HotUpdateAssets);
