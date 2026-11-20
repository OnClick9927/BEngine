using BEngine.Platform;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;

namespace BEngine.Editor.Build;

/// <summary>One project-asset boundary shared by the Player and hot-content builds.</summary>
internal static class PlayerProjectAssetPartition
{
    internal static bool IsResourcesAsset(string path)
    {
        var segments = Normalize(path).Split('/');
        return segments.Length > 2 && segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase) && !Query547(segments.Skip(1).SkipLast(1)) && Query678(segments.Skip(1).SkipLast(1));
        [global::BEngine.SkipLoopGuard]
        bool Query547(global::System.Collections.Generic.IEnumerable<string> querySource547)
        {
            ArgumentNullException.ThrowIfNull(querySource547, "source");
            bool Matches547(string segment) => segment.Equals("Editor", StringComparison.OrdinalIgnoreCase);
            global::System.ReadOnlySpan<string> queryValues547;
            if (querySource547.GetType() == typeof(string[]))
                queryValues547 = (string[])querySource547;
            else if (querySource547.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues547 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource547);
            else
            {
                foreach (var segment in querySource547)
                    if (Matches547(segment))
                        return true;
                return false;
            }
        
            foreach (var segment in queryValues547)
                if (Matches547(segment))
                    return true;
            return false;
        }
        
        [global::BEngine.SkipLoopGuard]
        bool Query678(global::System.Collections.Generic.IEnumerable<string> querySource678)
        {
            ArgumentNullException.ThrowIfNull(querySource678, "source");
            bool Matches678(string segment) => segment.Equals("Resources", StringComparison.OrdinalIgnoreCase);
            global::System.ReadOnlySpan<string> queryValues678;
            if (querySource678.GetType() == typeof(string[]))
                queryValues678 = (string[])querySource678;
            else if (querySource678.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues678 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource678);
            else
            {
                foreach (var segment in querySource678)
                    if (Matches678(segment))
                        return true;
                return false;
            }
        
            foreach (var segment in queryValues678)
                if (Matches678(segment))
                    return true;
            return false;
        }
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
