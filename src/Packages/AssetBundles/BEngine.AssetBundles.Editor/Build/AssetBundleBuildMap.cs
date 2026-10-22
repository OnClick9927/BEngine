using BEngine.Serialization;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

public static class AssetBundleBuildMap
{
    public static IReadOnlyList<AssetBundleBuildDefinition> FromImporters(
        ProjectWorkspace workspace,
        ProjectAssetDatabase database,
        IReadOnlyList<string>? includedAssetPaths = null,
        string? defaultBundleName = null) => FromImporters(
            workspace, database, includedAssetPaths, defaultBundleName,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static IReadOnlyList<AssetBundleBuildDefinition> FromImporters(
        ProjectWorkspace workspace, ProjectAssetDatabase database,
        IReadOnlyList<string>? includedAssetPaths, string? defaultBundleName,
        FileUtility fileSystem, PathUtility paths)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(database);
        var packages = AssetBundlePackageSettings.Load(workspace, fileSystem, paths);
        var qualifyBundleNames = packages.Packages.Count != 0;
        var selected = includedAssetPaths ?? AssetDependencyCollector.CollectAll(database, fileSystem, paths);
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, (string Package, List<string> Assets)>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetPath in selected.OrderBy(static path => path, StringComparer.Ordinal))
        {
            var path = assetPath.Replace('\\', '/');
            var owner = path;
            string? bundle = null;
            while (!string.IsNullOrEmpty(owner))
            {
                if (!settings.TryGetValue(owner, out bundle))
                {
                    var metaPath = workspace.ResolveInside(owner) + ".meta";
                    if (fileSystem.FileExists(metaPath))
                    {
                        var meta = YamlUtility.Deserialize<AssetMetaDocument>(fileSystem.ReadAllText(metaPath));
                        if (meta.Settings.TryGetValue("assetBundleName", out var name) &&
                            !string.IsNullOrWhiteSpace(name))
                        {
                            bundle = meta.Settings.TryGetValue("assetBundleVariant", out var variant) &&
                                     !string.IsNullOrWhiteSpace(variant) ? $"{name}.{variant}" : name;
                        }
                    }
                    settings.Add(owner, bundle);
                }
                if (!string.IsNullOrWhiteSpace(bundle)) break;
                var separator = owner.LastIndexOf('/');
                owner = separator < 0 ? string.Empty : owner[..separator];
            }
            bundle ??= defaultBundleName;
            if (string.IsNullOrWhiteSpace(bundle)) continue;
            var package = packages.ResolvePackageName(path);
            if (qualifyBundleNames) bundle = $"{package}--{bundle}";
            if (!groups.TryGetValue(bundle, out var group))
                groups.Add(bundle, group = (package, []));
            else if (!group.Package.Equals(package, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"AssetBundle packages '{group.Package}' and '{package}' produce " +
                                               $"the same bundle name '{bundle}'. Rename the package or importer bundle.");
            group.Assets.Add(path);
        }
        return groups.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new AssetBundleBuildDefinition
            {
                Name = pair.Key, PackageName = pair.Value.Package, AssetPaths = pair.Value.Assets
            })
            .ToArray();
    }
}
