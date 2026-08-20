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
            new("Assets", "Assets", Path.GetFullPath(assetsRoot), "Folder", true, false)
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Assets" };
        var assetItems = new List<ProjectBrowserItem>(assets.Count);
        foreach (var asset in assets)
        {
            var virtualPath = ProjectBrowserPath.Normalize(asset.AssetPath);
            if (virtualPath.Length == 0 || !seen.Add(virtualPath)) continue;
            var displayName = ProjectBrowserPath.DisplayName(Path.GetFileName(virtualPath), virtualPath,
                asset.SourcePath);
            assetItems.Add(new ProjectBrowserItem(virtualPath, displayName, asset.SourcePath,
                asset.AssetType, asset.IsDirectory, false, asset));
        }
        AppendAssetChildren(result, assetItems, "Assets", new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var enabled = packages.packages.Where(reference => reference.Enabled)
            .Select(reference => packages.TryGetDefinition(reference.Id, out var definition)
                ? (Reference: reference, Definition: definition, Name: DisplayName(definition))
                : (Reference: reference, Definition: (BPackageDefinition?)null, Name: reference.Id))
            .OrderBy(entry => entry.Definition is null ? 1 : 0)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        var packagesRoot = Path.GetFullPath(Path.Combine(assetsRoot, "..", "Packages"));
        result.Add(new ProjectBrowserItem("Packages", "Packages", packagesRoot, "Folder", true, true));
        foreach (var entry in enabled)
        {
            if (entry.Definition is not null) AddPackage(result, entry.Reference, entry.Definition);
            else AddMissingPackage(result, packagesRoot, entry.Reference);
        }
        return result.ToArray();
    }

    private static void AppendAssetChildren(
        ICollection<ProjectBrowserItem> result,
        IReadOnlyCollection<ProjectBrowserItem> assets,
        string parentPath,
        ISet<string> emitted)
    {
        foreach (var item in ProjectBrowserItemOrdering.Sort(assets.Where(asset =>
                     string.Equals(asset.ParentPath, parentPath, StringComparison.OrdinalIgnoreCase))))
        {
            if (!emitted.Add(item.NormalizedPath)) continue;
            result.Add(item);
            if (item.IsDirectory) AppendAssetChildren(result, assets, item.NormalizedPath, emitted);
        }
    }

    private static void AddPackage(
        ICollection<ProjectBrowserItem> result,
        PackageReferenceDocument reference,
        BPackageDefinition definition)
    {
        var packageRoot = Path.GetDirectoryName(Path.GetFullPath(definition.Path))!;
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
            Path.Combine(packagesRoot, reference.Id), "Missing Package", false, true,
            PackageId: reference.Id, PackageVersion: reference.Version));
    }

    private static void AddDirectory(
        ICollection<ProjectBrowserItem> result,
        string directory,
        string virtualParent,
        string packageId,
        string packageVersion)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .Where(Visible)
                .OrderByDescending(entry => entry is DirectoryInfo)
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
            var isDirectory = entry is DirectoryInfo;
            var displayName = ProjectBrowserPath.DisplayName(entry.Name, virtualPath, entry.FullName);
            result.Add(new ProjectBrowserItem(virtualPath, displayName, entry.FullName,
                isDirectory ? "Folder" : ResolveType(entry.FullName), isDirectory, true,
                PackageId: packageId, PackageVersion: packageVersion));
            if (isDirectory)
                AddDirectory(result, entry.FullName, virtualPath, packageId, packageVersion);
        }
    }

    private static bool Visible(FileSystemInfo entry)
    {
        if (entry.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (entry is DirectoryInfo && IgnoredDirectories.Contains(entry.Name)) return false;
        return (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0;
    }

    private static string DisplayName(BPackageDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Document.DisplayName)
            ? definition.Document.Id
            : definition.Document.DisplayName;

    private static string ResolveType(string path) => AssetTypeRegistry.Resolve(path) ??
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "Script",
            ".dll" => "Assembly",
            ".pdb" => "Debug Symbols",
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".svg" => "Image",
            ".shader" or ".glsl" => "Shader",
            ".yaml" or ".yml" or ".json" or ".xml" => "Data",
            ".md" or ".txt" => "Text",
            _ => "Package File"
        };
}
