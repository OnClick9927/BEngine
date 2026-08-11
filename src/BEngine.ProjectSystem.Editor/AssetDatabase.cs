using System.Security.Cryptography;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public enum AssetChangeKind
{
    Imported,
    Updated,
    Deleted,
    Moved
}

public sealed record AssetChange(AssetChangeKind Kind, Guid Guid, string AssetPath, string? PreviousPath = null);

public sealed record AssetRecord(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string MetaPath,
    string ArtifactPath,
    string AssetType,
    string SourceHash,
    bool IsDirectory);

public sealed class AssetDatabase
{
    private readonly ProjectWorkspace _workspace;
    private readonly Dictionary<Guid, AssetRecord> _byGuid = [];
    private readonly Dictionary<string, AssetRecord> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public event Action<IReadOnlyList<AssetChange>>? assetsChanged;
    public IReadOnlyCollection<AssetRecord> assets => _byGuid.Values;

    public AssetDatabase(ProjectWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public IReadOnlyList<AssetChange> Refresh()
    {
        var previous = LoadManifest().Assets
            .Where(item => Guid.TryParse(item.Guid, out _))
            .ToDictionary(item => Guid.Parse(item.Guid));
        var records = ScanAssets();
        _byGuid.Clear();
        _byPath.Clear();
        foreach (var record in records)
        {
            _byGuid[record.Guid] = record;
            _byPath[record.AssetPath] = record;
        }

        var changes = BuildChanges(previous, records);
        SaveManifest(records);
        if (changes.Count > 0) assetsChanged?.Invoke(changes);
        return changes;
    }

    public AssetRecord ImportAsset(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureInsideAssets(fullPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("Asset does not exist.", fullPath);
        }

        var record = Import(fullPath, Directory.Exists(fullPath));
        _byGuid[record.Guid] = record;
        _byPath[record.AssetPath] = record;
        SaveManifest(_byGuid.Values);
        assetsChanged?.Invoke([new AssetChange(AssetChangeKind.Imported, record.Guid, record.AssetPath)]);
        return record;
    }

    public string? AssetPathToGuid(string assetPath) =>
        _byPath.TryGetValue(NormalizeAssetPath(assetPath), out var record) ? record.Guid.ToString("N") : null;

    public string? GuidToAssetPath(Guid guid) => _byGuid.TryGetValue(guid, out var record) ? record.AssetPath : null;
    public AssetRecord? GetRecord(Guid guid) => _byGuid.GetValueOrDefault(guid);
    public AssetRecord? GetRecord(string assetPath) => _byPath.GetValueOrDefault(NormalizeAssetPath(assetPath));

    public IReadOnlyList<AssetRecord> FindAssets(string search)
    {
        search ??= string.Empty;
        return _byGuid.Values.Where(record =>
                record.AssetPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                record.AssetType.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<AssetRecord> ScanAssets()
    {
        var paths = Directory.EnumerateDirectories(_workspace.AssetsPath, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(_workspace.AssetsPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        return paths.Select(path => Import(path, Directory.Exists(path))).ToList();
    }

    private AssetRecord Import(string sourcePath, bool isDirectory)
    {
        var metaPath = sourcePath + ".meta";
        var meta = LoadOrCreateMeta(metaPath, sourcePath, isDirectory);
        var guid = Guid.Parse(meta.Guid);
        var assetPath = ToProjectPath(sourcePath);
        var hash = isDirectory ? string.Empty : ComputeHash(sourcePath);
        var artifactDirectory = Path.Combine(_workspace.AssetArtifactsPath, meta.Guid[..2]);
        Directory.CreateDirectory(artifactDirectory);
        var extension = isDirectory ? ".folder" : Path.GetExtension(sourcePath);
        var artifactPath = Path.Combine(artifactDirectory, meta.Guid + extension);

        if (!isDirectory && (!File.Exists(artifactPath) || !string.Equals(hash, meta.SourceHash, StringComparison.Ordinal)))
        {
            File.Copy(sourcePath, artifactPath, overwrite: true);
        }

        if (!string.Equals(meta.SourceHash, hash, StringComparison.Ordinal) ||
            !string.Equals(meta.AssetType, ResolveAssetType(sourcePath, isDirectory), StringComparison.Ordinal))
        {
            meta.SourceHash = hash;
            meta.AssetType = ResolveAssetType(sourcePath, isDirectory);
            meta.Importer = ResolveImporter(sourcePath, isDirectory);
            YamlUtility.Save(meta, metaPath);
        }

        return new AssetRecord(guid, assetPath, sourcePath, metaPath, artifactPath,
            meta.AssetType, hash, isDirectory);
    }

    private static AssetMetaDocument LoadOrCreateMeta(string metaPath, string sourcePath, bool isDirectory)
    {
        if (File.Exists(metaPath))
        {
            try
            {
                var existing = YamlUtility.Load<AssetMetaDocument>(metaPath);
                if (existing.Format == "BEngine.AssetMeta" && existing.Version == 1 && Guid.TryParse(existing.Guid, out _))
                {
                    return existing;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
            {
                BEngine.Debug.LogWarning($"Rebuilding invalid asset metadata for {sourcePath}: {exception.Message}");
            }
        }

        var document = new AssetMetaDocument
        {
            Guid = Guid.NewGuid().ToString("N"),
            AssetType = ResolveAssetType(sourcePath, isDirectory),
            Importer = ResolveImporter(sourcePath, isDirectory)
        };
        YamlUtility.Save(document, metaPath);
        return document;
    }

    private List<AssetChange> BuildChanges(
        IReadOnlyDictionary<Guid, AssetDatabaseEntryDocument> previous,
        IReadOnlyCollection<AssetRecord> current)
    {
        var changes = new List<AssetChange>();
        foreach (var record in current)
        {
            if (!previous.TryGetValue(record.Guid, out var old))
            {
                changes.Add(new AssetChange(AssetChangeKind.Imported, record.Guid, record.AssetPath));
            }
            else if (!string.Equals(old.AssetPath, record.AssetPath, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new AssetChange(AssetChangeKind.Moved, record.Guid, record.AssetPath, old.AssetPath));
            }
            else if (!string.Equals(old.SourceHash, record.SourceHash, StringComparison.Ordinal))
            {
                changes.Add(new AssetChange(AssetChangeKind.Updated, record.Guid, record.AssetPath));
            }
        }

        var currentGuids = current.Select(record => record.Guid).ToHashSet();
        changes.AddRange(previous.Where(pair => !currentGuids.Contains(pair.Key))
            .Select(pair => new AssetChange(AssetChangeKind.Deleted, pair.Key, pair.Value.AssetPath)));
        return changes;
    }

    private AssetDatabaseDocument LoadManifest()
    {
        if (!File.Exists(_workspace.AssetDatabasePath)) return new AssetDatabaseDocument();
        try
        {
            return YamlUtility.Load<AssetDatabaseDocument>(_workspace.AssetDatabasePath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            BEngine.Debug.LogWarning($"Asset database will be rebuilt: {exception.Message}");
            return new AssetDatabaseDocument();
        }
    }

    private void SaveManifest(IEnumerable<AssetRecord> records)
    {
        YamlUtility.Save(new AssetDatabaseDocument
        {
            Assets = records.OrderBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Select(record => new AssetDatabaseEntryDocument
                {
                    Guid = record.Guid.ToString("N"),
                    AssetPath = record.AssetPath,
                    AssetType = record.AssetType,
                    SourceHash = record.SourceHash,
                    ArtifactPath = Path.GetRelativePath(_workspace.LibraryPath, record.ArtifactPath).Replace('\\', '/')
                }).ToList()
        }, _workspace.AssetDatabasePath);
    }

    private void EnsureInsideAssets(string path)
    {
        if (!path.StartsWith(_workspace.AssetsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Asset path is outside the project Assets directory: {path}");
        }
    }

    private string ToProjectPath(string path) => Path.GetRelativePath(_workspace.RootPath, path).Replace('\\', '/');
    private static string NormalizeAssetPath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolveImporter(string path, bool isDirectory) => isDirectory ? "FolderImporter" :
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "ScriptImporter",
            ".png" or ".jpg" or ".jpeg" or ".bmp" => "TextureImporter",
            ".shader" or ".glsl" => "ShaderImporter",
            ".yaml" => "YamlImporter",
            _ => "DefaultImporter"
        };

    private static string ResolveAssetType(string path, bool isDirectory) => isDirectory ? "Folder" :
        path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase) ? "Scene" :
        path.EndsWith(".ui.yaml", StringComparison.OrdinalIgnoreCase) ? "UI Document" :
        path.EndsWith(".terrain.yaml", StringComparison.OrdinalIgnoreCase) ? "TerrainData" :
        path.EndsWith(".anim.yaml", StringComparison.OrdinalIgnoreCase) ? "AnimationClip" :
        path.EndsWith(".controller.yaml", StringComparison.OrdinalIgnoreCase) ? "AnimatorController" :
        path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase) ? "AssemblyDefinition" :
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "Script",
            ".png" or ".jpg" or ".jpeg" or ".bmp" => "Texture",
            ".shader" or ".glsl" => "Shader",
            ".yaml" => "YamlAsset",
            _ => "DefaultAsset"
        };
}

public sealed class AssetMetaDocument
{
    public string Format { get; set; } = "BEngine.AssetMeta";
    public int Version { get; set; } = 1;
    public string Guid { get; set; } = string.Empty;
    public string Importer { get; set; } = "DefaultImporter";
    public string AssetType { get; set; } = "DefaultAsset";
    public string SourceHash { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];
}

public sealed class AssetDatabaseDocument
{
    public string Format { get; set; } = "BEngine.AssetDatabase";
    public int Version { get; set; } = 1;
    public List<AssetDatabaseEntryDocument> Assets { get; set; } = [];
}

public sealed class AssetDatabaseEntryDocument
{
    public string Guid { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string ArtifactPath { get; set; } = string.Empty;
}
