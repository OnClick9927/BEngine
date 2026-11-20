using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class ProjectBrowserTreeBuilder
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".artifacts", "artifacts", "bin", "obj"
    };

    internal static ProjectBrowserItem[] Build(
        string assetsRoot,
        IReadOnlyList<AssetRecord> assets,
        BPackageManager packages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(packages);

        var result = new List<ProjectBrowserItem>(assets.Count + 32)
        {
            new("Assets", "Assets", EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(assetsRoot), "Folder", true, false)
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Assets" };
        var assetItems = new List<ProjectBrowserItem>(assets.Count);
        var mainAssets = assets.Where(asset => !asset.IsSubAsset).ToArray();
        var childrenByParent = assets.Where(asset => asset.ParentGuid.HasValue)
            .GroupBy(asset => asset.ParentGuid!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var asset in mainAssets)
        {
            var virtualPath = ProjectBrowserPath.Normalize(asset.AssetPath);
            if (virtualPath.Length == 0 || !seen.Add(virtualPath)) continue;
            var displayName = ProjectBrowserPath.DisplayName(EditorCapabilities.GetRequiredService<PathUtility>().GetFileName(virtualPath), virtualPath,
                asset.SourcePath);
            var ownerItem = new ProjectBrowserItem(virtualPath, displayName, asset.SourcePath,
                asset.AssetType, asset.IsDirectory, false, asset);
            assetItems.Add(ownerItem);
            if (!asset.IsDirectory)
                AppendSubAssets(assetItems, ownerItem, asset,
                    childrenByParent.GetValueOrDefault(asset.Guid) ?? []);
        }
        var itemsByParent = assetItems.ToLookup(item => item.TreeParentKey, StringComparer.OrdinalIgnoreCase);
        AppendAssetChildren(result, itemsByParent, "Assets", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var enabled = packages.packages.Where(reference => reference.Enabled)
            .Select(reference => packages.TryGetDefinition(reference.Id, out var definition)
                ? (Reference: reference, Definition: definition, Name: DisplayName(definition))
                : (Reference: reference, Definition: (BPackageDefinition?)null, Name: reference.Id))
            .OrderBy(entry => entry.Definition is null ? 1 : 0)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        var packagesRoot = EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(EditorCapabilities.GetRequiredService<PathUtility>().Combine(assetsRoot, "..", "Packages"));
        result.Add(new ProjectBrowserItem("Packages", "Packages", packagesRoot, "Folder", true, true));
        foreach (var entry in enabled)
        {
            if (entry.Definition?.Document.Type == "project") continue;
            if (entry.Definition is not null) AddPackage(result, entry.Reference, entry.Definition);
            else AddMissingPackage(result, packagesRoot, entry.Reference);
        }
        return result.ToArray();
    }

    private static void AppendAssetChildren(
        ICollection<ProjectBrowserItem> result,
        ILookup<string?, ProjectBrowserItem> itemsByParent,
        string parentPath,
        ISet<string> emitted)
    {
        foreach (var item in ProjectBrowserItemOrdering.Sort(itemsByParent[parentPath]))
        {
            if (!emitted.Add(item.BrowserKey)) continue;
            result.Add(item);
            AppendAssetChildren(result, itemsByParent, item.BrowserKey, emitted);
        }
    }

    private static void AppendSubAssets(
        ICollection<ProjectBrowserItem> items,
        ProjectBrowserItem ownerItem,
        AssetRecord owner,
        IReadOnlyList<AssetRecord> importedChildren)
    {
        BObject[] representations;
        try
        {
            if (importedChildren.Count == 0 && !AssetDatabase.MayHaveAssetRepresentations(owner.SourcePath))
                return;
            representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(owner.AssetPath);
        }
        catch (Exception exception)
        {
            EditorCallbacks.Report($"Project.LoadSubAssets {owner.AssetPath}", exception);
            representations = [];
        }

        var represented = new HashSet<long>();
        foreach (var child in importedChildren.OrderBy(item => item.LocalIdentifier))
        {
            var value = Matches4931();
            [global::BEngine.SkipLoopGuard]
            global::BEngine.BObject? Matches4931()
            {
                var matchSource4931 = representations;
                ArgumentNullException.ThrowIfNull(matchSource4931, "source");
                global::System.ReadOnlySpan<global::BEngine.BObject> matchValues4931 = matchSource4931;
                foreach (var item in matchValues4931)
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out var guid, out var localId) && Guid.TryParse(guid, out var parentGuid) && parentGuid == owner.Guid && localId == child.LocalIdentifier)
                        return item;
                }
            
                return default!;
            }
            value ??= AssetDatabase.LoadMainAssetAtPath(child.AssetPath);
            if (value is null) continue;
            represented.Add(child.LocalIdentifier);
            items.Add(CreateSubAssetItem(ownerItem, child.AssetPath, child.SourcePath,
                child.AssetType, child.LocalIdentifier, value, child));
        }

        foreach (var value in representations)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out var guid, out var localId) ||
                !Guid.TryParse(guid, out var parentGuid) || parentGuid != owner.Guid ||
                localId <= 0 || !represented.Add(localId)) continue;
            items.Add(CreateSubAssetItem(ownerItem, owner.AssetPath, owner.SourcePath,
                ObjectNames.NicifyVariableName(value.GetType().Name), localId, value, null));
        }
    }

    private static ProjectBrowserItem CreateSubAssetItem(
        ProjectBrowserItem owner,
        string assetPath,
        string sourcePath,
        string assetType,
        long localIdentifier,
        BObject value,
        AssetRecord? record)
    {
        var displayName = string.IsNullOrWhiteSpace(value.name)
            ? ObjectNames.NicifyVariableName(value.GetType().Name)
            : value.name.Trim();
        return new ProjectBrowserItem(assetPath, displayName, sourcePath, assetType,
            false, false, record)
        {
            BrowserKey = $"{owner.BrowserKey}#subasset={localIdentifier}",
            BrowserParentKey = owner.BrowserKey,
            SubAssetObject = value,
            SubAssetIcon = EditorIconRegistry.GetIconPath(value.GetType())
        };
    }

    private static void AddPackage(
        ICollection<ProjectBrowserItem> result,
        PackageReferenceDocument reference,
        BPackageDefinition definition)
    {
        var packageRoot = EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(definition.Path))!;
        var virtualRoot = $"Packages/{definition.Document.Id}";
        result.Add(new ProjectBrowserItem(virtualRoot, DisplayName(definition), packageRoot, "Package", true,
            true, PackageId: definition.Document.Id, PackageVersion: reference.Version));
        AddDirectory(result, packageRoot, virtualRoot, definition.Document.Id, reference.Version);
    }

    private static void AddMissingPackage(
        ICollection<ProjectBrowserItem> result,
        string packagesRoot,
        PackageReferenceDocument reference)
    {
        result.Add(new ProjectBrowserItem($"Packages/{reference.Id}", $"{reference.Id} (Missing)",
            EditorCapabilities.GetRequiredService<PathUtility>().Combine(packagesRoot, reference.Id), "Missing Package", false, true,
            PackageId: reference.Id, PackageVersion: reference.Version));
    }

    private static void AddDirectory(
        ICollection<ProjectBrowserItem> result,
        string directory,
        string virtualParent,
        string packageId,
        string packageVersion)
    {
        BrowserEntry[] entries;
        try
        {
            entries = EditorCapabilities.GetRequiredService<FileUtility>()
                .EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(path => CreateEntry(path, isDirectory: true))
                .Concat(EditorCapabilities.GetRequiredService<FileUtility>()
                    .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Select(path => CreateEntry(path, isDirectory: false)))
                .Where(Visible)
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var virtualPath = ProjectBrowserPath.Normalize($"{virtualParent}/{entry.Name}");
            var displayName = ProjectBrowserPath.DisplayName(entry.Name, virtualPath, entry.Path);
            result.Add(new ProjectBrowserItem(virtualPath, displayName, entry.Path,
                entry.IsDirectory ? "Folder" : ResolveType(entry.Path), entry.IsDirectory, true,
                PackageId: packageId, PackageVersion: packageVersion));
            if (entry.IsDirectory)
                AddDirectory(result, entry.Path, virtualPath, packageId, packageVersion);
        }
    }

    private static BrowserEntry CreateEntry(string path, bool isDirectory) => new(
        path,
        EditorCapabilities.GetRequiredService<PathUtility>().GetFileName(
            EditorCapabilities.GetRequiredService<PathUtility>().TrimEndingDirectorySeparator(path)),
        isDirectory,
        EditorCapabilities.GetRequiredService<FileUtility>().GetAttributes(path));

    private static bool Visible(BrowserEntry entry)
    {
        if (entry.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (entry.IsDirectory && IgnoredDirectories.Contains(entry.Name)) return false;
        return (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0;
    }

    private static string DisplayName(BPackageDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Document.DisplayName)
            ? definition.Document.Id
            : definition.Document.DisplayName;

    private static string ResolveType(string path) => AssetTypeRegistry.Resolve(path) ??
        EditorCapabilities.GetRequiredService<PathUtility>().GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "Script",
            ".dll" => "Assembly",
            ".pdb" => "Debug Symbols",
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" => "Texture",
            ".jpg" or ".jpeg" or ".bmp" or ".svg" => "Package File",
            ".shader" or ".cg" => "Shader",
            ".yaml" or ".yml" or ".json" or ".xml" => "Data",
            ".md" or ".txt" => "Text",
            _ => "Package File"
        };

    private readonly record struct BrowserEntry(
        string Path,
        string Name,
        bool IsDirectory,
        FileAttributes Attributes);
}
