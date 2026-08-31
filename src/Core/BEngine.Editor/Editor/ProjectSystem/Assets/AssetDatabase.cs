using System.Security.Cryptography;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed class AssetDatabase
{
    private readonly ProjectWorkspace _workspace;
    private readonly Dictionary<Guid, AssetRecord> _byGuid = [];
    private readonly Dictionary<string, AssetRecord> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AssetRecord[] _assetSnapshot = [];
    private long _revision;

    public event Action<IReadOnlyList<AssetChange>>? assetsChanged;
    public IReadOnlyCollection<AssetRecord> assets
    {
        get
        {
            return _assetSnapshot;
        }
    }

    public AssetDatabase(ProjectWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public IReadOnlyList<AssetChange> Refresh(Action<AssetScanProgress>? progress = null)
    {
        return ApplyRefresh(PrepareRefresh(progress));
    }

    internal AssetRefreshSnapshot PrepareRefresh(
        Action<AssetScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _refreshGate.Wait(cancellationToken);
        try
        {
            var baseRevision = Volatile.Read(ref _revision);
            var previous = LoadManifest().Assets
                .Where(item => Guid.TryParse(item.Guid, out _))
                .ToDictionary(item => Guid.Parse(item.Guid));
            var records = ScanAssets(progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var changes = BuildChanges(previous, records);
            SaveManifest(records);
            return new AssetRefreshSnapshot(baseRevision, records, changes);
        }
        finally { _refreshGate.Release(); }
    }

    internal IReadOnlyList<AssetChange> ApplyRefresh(AssetRefreshSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryApplyRefresh(snapshot, out var changes))
            throw new InvalidOperationException(
                "The asset refresh snapshot is stale because a newer asset operation already completed.");
        return changes;
    }

    internal bool TryApplyRefresh(
        AssetRefreshSnapshot snapshot,
        out IReadOnlyList<AssetChange> changes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.BaseRevision != Volatile.Read(ref _revision))
        {
            changes = [];
            return false;
        }
        _byGuid.Clear();
        _byPath.Clear();
        foreach (var record in snapshot.Records)
        {
            _byGuid[record.Guid] = record;
            _byPath[record.AssetPath] = record;
        }
        _assetSnapshot = snapshot.Records.ToArray();

        Interlocked.Increment(ref _revision);
        changes = snapshot.Changes;
        if (changes.Count > 0)
            BEngine.Editor.EditorCallbackDispatcher.Invoke(
                assetsChanged, changes, nameof(assetsChanged));
        return true;
    }

    public AssetRecord ImportAsset(string path)
    {
        _refreshGate.Wait();
        try
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
            _assetSnapshot = _byGuid.Values.ToArray();
            SaveManifest(_byGuid.Values);
            Interlocked.Increment(ref _revision);
            BEngine.Editor.EditorCallbackDispatcher.Invoke(assetsChanged,
                (IReadOnlyList<AssetChange>)[new AssetChange(AssetChangeKind.Imported, record.Guid, record.AssetPath)],
                nameof(assetsChanged));
            return record;
        }
        finally { _refreshGate.Release(); }
    }

    public string? AssetPathToGuid(string assetPath)
    {
        return _byPath.TryGetValue(NormalizeAssetPath(assetPath), out var record)
            ? record.Guid.ToString("N")
            : null;
    }

    public string? GuidToAssetPath(Guid guid)
    {
        return _byGuid.TryGetValue(guid, out var record) ? record.AssetPath : null;
    }

    public AssetRecord? GetRecord(Guid guid)
    {
        return _byGuid.GetValueOrDefault(guid);
    }

    public AssetRecord? GetRecord(string assetPath)
    {
        return _byPath.GetValueOrDefault(NormalizeAssetPath(assetPath));
    }

    public IReadOnlyList<AssetRecord> FindAssets(string search)
    {
        search ??= string.Empty;
        return _byGuid.Values.Where(record =>
                record.AssetPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                record.AssetType.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<AssetRecord> ScanAssets(
        Action<AssetScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var paths = Directory.EnumerateDirectories(_workspace.AssetsPath, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(_workspace.AssetsPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var records = new List<AssetRecord>(paths.Length);
        if (paths.Length == 0)
            BEngine.Editor.EditorCallbackDispatcher.Invoke(progress,
                new AssetScanProgress(0, 0, string.Empty), "AssetDatabase.scanProgress");
        for (var index = 0; index < paths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = paths[index];
            records.Add(Import(path, Directory.Exists(path)));
            BEngine.Editor.EditorCallbackDispatcher.Invoke(progress,
                new AssetScanProgress(index + 1, paths.Length, ToProjectPath(path)),
                "AssetDatabase.scanProgress");
        }
        return records;
    }

    private AssetRecord Import(string sourcePath, bool isDirectory)
    {
        var metaPath = sourcePath + ".meta";
        var meta = LoadOrCreateMeta(metaPath, sourcePath, isDirectory);
        var guid = Guid.Parse(meta.Guid);
        var assetPath = ToProjectPath(sourcePath);
        var hash = isDirectory ? string.Empty : ComputeHash(sourcePath);
        var resolvedAssetType = ResolveAssetType(sourcePath, isDirectory);
        var resolvedImporter = ResolveImporter(sourcePath, isDirectory);
        var artifactDirectory = Path.Combine(_workspace.AssetArtifactsPath, meta.Guid[..2]);
        Directory.CreateDirectory(artifactDirectory);
        var extension = isDirectory ? ".folder" : Path.GetExtension(sourcePath);
        var artifactPath = Path.Combine(artifactDirectory, meta.Guid + extension);

        if (!isDirectory && (!File.Exists(artifactPath) || !string.Equals(hash, meta.SourceHash, StringComparison.Ordinal)))
        {
            using var artifactLock = AcquireWriteLock(artifactPath + ".lock");
            if (!File.Exists(artifactPath) || !string.Equals(hash, meta.SourceHash, StringComparison.Ordinal))
                File.Copy(sourcePath, artifactPath, overwrite: true);
        }

        if (!string.Equals(meta.SourceHash, hash, StringComparison.Ordinal) ||
            !string.Equals(meta.AssetType, resolvedAssetType, StringComparison.Ordinal) ||
            !string.Equals(meta.Importer, resolvedImporter, StringComparison.Ordinal))
        {
            meta.SourceHash = hash;
            meta.AssetType = resolvedAssetType;
            meta.Importer = resolvedImporter;
            meta.Save(metaPath);
        }

        var parentGuid = Guid.TryParse(meta.ParentGuid, out var parsedParent) ? parsedParent : (Guid?)null;
        return new AssetRecord(guid, assetPath, sourcePath, metaPath, artifactPath,
            meta.AssetType, hash, isDirectory, parentGuid, Math.Max(0, meta.LocalIdentifier));
    }

    private static AssetMetaDocument LoadOrCreateMeta(string metaPath, string sourcePath, bool isDirectory)
    {
        using var writeLock = AcquireWriteLock(metaPath + ".lock");
        if (File.Exists(metaPath))
        {
            try
            {
                var existing = Document.Load<AssetMetaDocument>(metaPath);
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
        document.Save(metaPath);
        return document;
    }

    private static FileStream AcquireWriteLock(string path)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(15);
            }
        }
    }

    private static List<AssetChange> BuildChanges(
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
            return Document.Load<AssetDatabaseDocument>(_workspace.AssetDatabasePath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            BEngine.Debug.LogWarning($"Asset database will be rebuilt: {exception.Message}");
            return new AssetDatabaseDocument();
        }
    }

    private void SaveManifest(IEnumerable<AssetRecord> records)
    {
        new AssetDatabaseDocument
        {
            Assets = records.OrderBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Select(record => new AssetDatabaseEntryDocument
                {
                    Guid = record.Guid.ToString("N"),
                    AssetPath = record.AssetPath,
                    AssetType = record.AssetType,
                    SourceHash = record.SourceHash,
                    ArtifactPath = Path.GetRelativePath(_workspace.LibraryPath, record.ArtifactPath).Replace('\\', '/'),
                    ParentGuid = record.ParentGuid?.ToString("N") ?? string.Empty,
                    LocalIdentifier = record.LocalIdentifier
                }).ToList()
        }.Save(_workspace.AssetDatabasePath);
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
        BEngine.Editor.AssetTypeRegistry.ResolveImporterName(path) is { } registeredImporter
            ? registeredImporter
            : path.EndsWith(".prefab.yaml", StringComparison.OrdinalIgnoreCase) ? "PrefabImporter" :
                Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".cs" => "ScriptImporter",
                    ".html" or ".htm" => "HtmlImporter",
                    ".png" => "TextureImporter",
                    ".shader" or ".glsl" => "ShaderImporter",
                    ".bpackage" => "BPackageImporter",
                    ".yaml" => "YamlImporter",
                    _ => "DefaultImporter"
                };

    private static string ResolveAssetType(string path, bool isDirectory) => isDirectory ? "Folder" :
        path.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase) ? ResolveManagedAssetType(path) :
        BEngine.Editor.AssetTypeRegistry.Resolve(path) is { } extensionType ? extensionType :
        path.EndsWith(".prefab.yaml", StringComparison.OrdinalIgnoreCase) ? "Prefab" :
        path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase) ? "Scene" :
        path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase) ? "UI Document" :
        path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase) ? "UI Style Sheet" :
        path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ? "HTML Document" :
        path.EndsWith(".ui.yaml", StringComparison.OrdinalIgnoreCase) ? "Legacy UI Document" :
        path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase) ? "AssemblyDefinition" :
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "Script",
            ".png" => "Texture",
            ".shader" or ".glsl" => "Shader",
            ".json" => "JSON",
            ".xml" => "XML",
            ".md" => "Markdown",
            ".txt" => "Text",
            ".bpackage" => "BEnginePackage",
            ".yaml" => "YamlAsset",
            _ => "DefaultAsset"
        };

    private static string ResolveManagedAssetType(string path)
    {
        try
        {
            var document = Document.Load<ManagedAssetDocument>(path);
            if (document.Format == "BEngine.ManagedAsset" && !string.IsNullOrWhiteSpace(document.TypeName))
                return document.TypeName.Split(',')[0].Split('.').Last();
        }
        catch { }
        return "ManagedAsset";
    }
}
