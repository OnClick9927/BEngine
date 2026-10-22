using BEngine.ProjectSystem;
using BEngine.AssetBundles;
using BEngine.Content;
using BEngine.ProjectSystem.Editor;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;

namespace BEngine.Editor;

internal static class PlayerResourceStripper
{
    internal static IReadOnlyList<string> GetProjectRoots(
        IEnumerable<string> scenes, IEnumerable<string> availableAssets,
        IEnumerable<string> assemblyPaths, PlayerStrippingSettings settings,
        IEnumerable<ProjectAssetRecord>? records = null,
        IEnumerable<string>? additionalResourceFiles = null)
    {
        var assets = availableAssets.ToArray();
        var roots = scenes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Resources are a packaging promise, including files reached only through
        // generated/runtime addresses. Full stripping must keep these roots too.
        roots.UnionWith(assets.Where(PlayerProjectAssetPartition.IsResourcesAsset));
        var literals = PlayerManagedCodeTrimmer.GetStringConstants(assemblyPaths);
        var texts = (additionalResourceFiles ?? []).Where(PlayerManagedCodeTrimmer.IsTextAsset)
            .Select(File.ReadAllText).ToArray();
        foreach (var asset in assets)
        {
            var normalized = asset.Replace('\\', '/');
            var marker = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            var resourcePath = marker < 0 ? null : normalized[(marker + "/Resources/".Length)..];
            var resourceStem = resourcePath is null ? null : resourcePath[..(resourcePath.LastIndexOf('/') + 1)] +
                Path.GetFileName(resourcePath).Split('.')[0];
            if (literals.Contains(normalized) || resourcePath is not null &&
                (literals.Contains(resourcePath) || literals.Contains(resourceStem!)) ||
                literals.Any(value => value.StartsWith("Assets/AOT/", StringComparison.OrdinalIgnoreCase) && IsWithin(normalized, value) ||
                                      resourcePath is not null && IsWithin(resourcePath, value)) ||
                texts.Any(text => text.Contains(normalized, StringComparison.OrdinalIgnoreCase))) roots.Add(asset);
        }
        if (records is not null)
        {
            var assetSet = assets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var guids = literals.Select(static value => value.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) ? value[5..] : value)
                .Select(static value => value.Split('#')[0])
                .Where(static value => Guid.TryParse(value, out _)).Select(Guid.Parse).ToHashSet();
            foreach (var record in records)
                if (assetSet.Contains(record.AssetPath) && (guids.Contains(record.Guid) ||
                    texts.Any(text => text.Contains(record.Guid.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
                                      text.Contains(record.Guid.ToString("D"), StringComparison.OrdinalIgnoreCase))))
                    roots.Add(record.AssetPath);
        }
        foreach (var preserved in settings.PreservedAssets.Where(static p =>
                     p.StartsWith("Assets/AOT/", StringComparison.OrdinalIgnoreCase) ||
                     p.Equals("Assets/AOT", StringComparison.OrdinalIgnoreCase)))
        {
            var matched = assets.Where(asset => IsWithin(asset, preserved)).ToArray();
            if (matched.Length == 0) throw new InvalidDataException($"Preserved asset was not found: '{preserved}'.");
            roots.UnionWith(matched);
        }
        return roots.OrderBy(static p => p, StringComparer.Ordinal).ToArray();
    }

    internal static RuntimeManagedCodeReleaseInputSet FilterPackageResources(
        RuntimeManagedCodeReleaseInputSet inputs, IEnumerable<string> selectedAssetFiles,
        PlayerStrippingSettings settings, PlayerStrippingReport report, bool coreResources = false,
        IEnumerable<string>? additionalAssemblyPaths = null)
    {
        var literals = PlayerManagedCodeTrimmer.GetStringConstants(inputs.Assemblies.Select(static a => a.AssemblyPath)
            .Concat(additionalAssemblyPaths ?? []));
        var texts = selectedAssetFiles.Where(PlayerManagedCodeTrimmer.IsTextAsset).Select(File.ReadAllText).ToList();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resources = inputs.PackageResources.ToArray();
        if (coreResources)
            foreach (var resource in resources.Where(static r => r.Address.Contains("/Shaders/Startup/", StringComparison.Ordinal)))
                Include(resource);
        foreach (var preserved in settings.PreservedAssets.Where(p =>
                     p.StartsWith(coreResources ? PlayerPackagedResourceAddresses.CoreResourcesPrefix : "Assets/Packages/", StringComparison.OrdinalIgnoreCase)))
        {
            var matched = resources.Where(resource => IsWithin(resource.Address, preserved)).ToArray();
            if (matched.Length == 0) throw new InvalidDataException($"Preserved package resource was not found: '{preserved}'.");
            foreach (var resource in matched) Include(resource);
        }
        // A growing queue reaches references inside retained package resources as well.
        // Paths and program-name literals also cover shader families selected at runtime.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var resource in resources)
            {
                if (included.Contains(resource.Address)) continue;
                var path = resource.Address.Replace('\\', '/');
                var name = Path.GetFileName(path);
                var stem = name.Split('.')[0];
                if (!literals.Any(value => path.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                                           path.EndsWith('/' + value.TrimStart('/'), StringComparison.OrdinalIgnoreCase) ||
                                           path.Contains('/' + value.Trim('/') + '.', StringComparison.OrdinalIgnoreCase) ||
                                           name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                                           stem.Equals(value, StringComparison.OrdinalIgnoreCase)) &&
                    !texts.Any(text => text.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                                       text.Contains(name, StringComparison.OrdinalIgnoreCase))) continue;
                Include(resource); changed = true;
            }
        }
        foreach (var resource in resources)
        {
            var kept = included.Contains(resource.Address);
            report.Resources.Add(new PlayerStrippingReportEntry
            {
                Name = resource.Address, OriginalBytes = resource.Size,
                PublishedBytes = kept ? resource.Size : 0, Included = kept,
                Reason = kept ? "Resource reference, runtime literal or explicit preservation" : "No reachable resource reference"
            });
        }
        var filtered = resources.Where(resource => included.Contains(resource.Address)).ToArray();
        return new RuntimeManagedCodeReleaseInputSet(inputs.Assemblies, filtered, inputs.PackageResourceRoots);

        void Include(RuntimePackageResourceInput resource)
        {
            if (!included.Add(resource.Address)) return;
            if (PlayerManagedCodeTrimmer.IsTextAsset(resource.FilePath)) texts.Add(File.ReadAllText(resource.FilePath));
        }
    }

    internal static void RecordProjectAssets(IEnumerable<ProjectAssetRecord> available,
        IReadOnlySet<string> selected, PlayerStrippingReport report)
    {
        foreach (var asset in available.Where(static a => !a.IsDirectory))
        {
            var kept = selected.Contains(asset.AssetPath);
            var size = File.Exists(asset.ArtifactPath) ? new FileInfo(asset.ArtifactPath).Length : 0;
            report.Resources.Add(new PlayerStrippingReportEntry
            {
                Name = asset.IsSubAsset
                    ? AssetBundleValidation.CreateSubAssetAddress(asset.ParentGuid!.Value, asset.LocalIdentifier)
                    : asset.AssetPath,
                OriginalBytes = size, PublishedBytes = kept ? size : 0,
                Included = kept, Reason = kept ? "Startup scene dependency, runtime literal or explicit preservation" : "Not reachable from main-package roots"
            });
        }
    }

    private static bool IsWithin(string value, string root) =>
        value.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(root.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase);
}
