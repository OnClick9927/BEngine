using BEngine.Hosting;
using BEngine.ProjectSystem;
using BEngine.AssetBundles;
using BEngine.ProjectSystem.Editor;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;

namespace BEngine.Editor.Build;

internal static class PlayerResourceStripper
{
    internal static IReadOnlyList<string> GetProjectRoots(
        IEnumerable<string> scenes, IEnumerable<string> availableAssets,
        IEnumerable<string> assemblyPaths, PlayerStrippingSettings settings,
        IEnumerable<ProjectAssetRecord>? records = null,
        IEnumerable<string>? additionalResourceFiles = null)
    {
        ValidatePreservation(settings);
        var assets = availableAssets.ToArray();
        var roots = scenes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // These roots are this content provider's packaging promise.
        roots.UnionWith(assets.Where(PlayerProjectAssetPartition.IsResourcesAsset));
        var references = PlayerResourceReferenceSet.Read(assemblyPaths, additionalResourceFiles);
        foreach (var asset in assets)
        {
            var normalized = asset.Replace('\\', '/');
            var marker = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            var alias = marker < 0 ? null : normalized[(marker + "/Resources/".Length)..];
            if (references.ReferencesAssetPath(normalized, alias, "Assets/AOT/")) roots.Add(asset);
        }
        if (records is not null)
        {
            var assetSet = assets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
                if (assetSet.Contains(record.AssetPath) && references.ReferencesGuid(record.Guid)) roots.Add(record.AssetPath);
        }
        foreach (var preserved in settings.PreservedAssets.Where(static path =>
                     path.StartsWith("Assets/AOT/", StringComparison.OrdinalIgnoreCase) ||
                     path.Equals("Assets/AOT", StringComparison.OrdinalIgnoreCase)))
        {
            var matched = assets.Where(asset => IsWithin(asset, preserved)).ToArray();
            if (matched.Length == 0) throw new InvalidDataException($"Preserved asset was not found: '{preserved}'.");
            roots.UnionWith(matched);
        }
        return roots.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
    }

    internal static RuntimeManagedCodeReleaseInputSet FilterPackageResources(
        RuntimeManagedCodeReleaseInputSet inputs, IEnumerable<string> selectedAssetFiles,
        PlayerStrippingSettings settings, PlayerStrippingReport report, bool coreResources = false,
        IEnumerable<string>? additionalAssemblyPaths = null)
    {
        ValidatePreservation(settings);
        var references = PlayerResourceReferenceSet.Read(inputs.Assemblies.Select(static input => input.AssemblyPath)
            .Concat(additionalAssemblyPaths ?? []), selectedAssetFiles);
        var preserved = settings.PreservedAssets.Where(path => path.StartsWith(
            coreResources ? HostPackagedResourceAddresses.CoreResourcesPrefix : "Assets/Packages/", StringComparison.OrdinalIgnoreCase));
        var resources = references.SelectResources(inputs.PackageResources, preserved, report,
            coreResources ? static resource => ShaderBuildPipeline.IsRequiredBootstrapResource(resource.Address) : null);
        return new(inputs.Assemblies, resources, inputs.PackageResourceRoots);
    }

    private static void ValidatePreservation(PlayerStrippingSettings settings)
    {
        foreach (var path in settings.PreservedAssets)
            if (!(path.StartsWith("Assets/AOT/", StringComparison.OrdinalIgnoreCase) ||
                  path.Equals("Assets/AOT", StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith(HostPackagedResourceAddresses.CoreResourcesPrefix, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"This content provider's preserved assets must be below Assets/AOT, Assets/Packages or host Assets/Resources: '{path}'.");
    }

    internal static void RecordProjectAssets(IEnumerable<ProjectAssetRecord> available,
        IReadOnlySet<string> selected, PlayerStrippingReport report)
    {
        {
            var __filteredSource7899 = available;
            ArgumentNullException.ThrowIfNull(__filteredSource7899, "source");
            foreach (var asset in __filteredSource7899)
            {
                if (!(!asset.IsDirectory))
                    continue;
                var kept = selected.Contains(asset.AssetPath);
                var size = File.Exists(asset.ArtifactPath) ? new FileInfo(asset.ArtifactPath).Length : 0;
                report.Resources.Add(new PlayerStrippingReportEntry { Name = asset.IsSubAsset ? AssetBundleValidation.CreateSubAssetAddress(asset.ParentGuid!.Value, asset.LocalIdentifier) : asset.AssetPath, OriginalBytes = size, PublishedBytes = kept ? size : 0, Included = kept, Reason = kept ? "Startup scene dependency, runtime literal or explicit preservation" : "Not reachable from main-package roots" });
            }
        }
    }

    private static bool IsWithin(string value, string root) =>
        value.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(root.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase);
}
