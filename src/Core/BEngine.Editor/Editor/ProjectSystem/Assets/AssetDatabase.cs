using System.Security.Cryptography;
using System.Text;
using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed class AssetDatabase
{
    private readonly ProjectWorkspace _workspace;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly Dictionary<Guid, AssetRecord> _byGuid = [];
    private readonly Dictionary<string, AssetRecord> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AssetRecord[] _assetSnapshot = [];
    private long _revision;
    private AssetHashBatch? _hashBatch;
    public AssetHashStatistics lastHashStatistics { get; private set; }

    public event Action<IReadOnlyList<AssetChange>>? assetsChanged;
    public IReadOnlyCollection<AssetRecord> assets
    {
        get
        {
            return _assetSnapshot;
        }
    }

    public AssetDatabase(ProjectWorkspace workspace) : this(
        workspace,
        RuntimeServiceAccess.FileSystem,
        RuntimeServiceAccess.PathService,
        RuntimeServiceAccess.PlatformService)
    {
    }

    public AssetDatabase(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService) : this(
        workspace,
        fileSystem,
        pathService,
        RuntimeServiceAccess.PlatformService)
    {
    }

    public AssetDatabase(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
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
            _hashBatch = new(_fileSystem, _pathService, cancellationToken);
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
        finally { CompleteHashBatch(); _refreshGate.Release(); }
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
        _assetSnapshot = CreateSortedSnapshot(snapshot.Records);
        _byGuid.Clear();
        _byPath.Clear();
        foreach (var record in _assetSnapshot)
        {
            _byGuid[record.Guid] = record;
            if (!record.IsSubAsset) _byPath[record.AssetPath] = record;
        }

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
            _hashBatch = new(_fileSystem, _pathService, CancellationToken.None);
            var fullPath = _pathService.GetFullPath(path);
            EnsureInsideAssets(fullPath);
            if (!_fileSystem.FileExists(fullPath) && !_fileSystem.DirectoryExists(fullPath))
            {
                throw new FileNotFoundException("Asset does not exist.", fullPath);
            }

            var previous = _assetSnapshot.ToDictionary(static record => record.Guid,
                ToManifestEntry);
            var record = Import(fullPath, _fileSystem.DirectoryExists(fullPath));
            var records = _byGuid.Values.Where(candidate => candidate.Guid != record.Guid &&
                    candidate.ParentGuid != record.Guid &&
                    !candidate.AssetPath.Equals(record.AssetPath, StringComparison.OrdinalIgnoreCase))
                .Append(record)
                .ToList();
            AppendGeneratedArtifacts(records, CancellationToken.None, record.Guid);
            var changes = BuildChanges(previous, records);

            _assetSnapshot = CreateSortedSnapshot(records);
            _byGuid.Clear();
            _byPath.Clear();
            foreach (var current in _assetSnapshot)
            {
                _byGuid[current.Guid] = current;
                if (!current.IsSubAsset) _byPath[current.AssetPath] = current;
            }
            SaveManifest(_assetSnapshot);
            Interlocked.Increment(ref _revision);
            if (changes.Count > 0)
                BEngine.Editor.EditorCallbackDispatcher.Invoke(assetsChanged,
                    (IReadOnlyList<AssetChange>)changes, nameof(assetsChanged));
            return record;
        }
        finally { CompleteHashBatch(); _refreshGate.Release(); }
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
        if (search.Length == 0) return _assetSnapshot;

        var matches = new List<AssetRecord>();
        foreach (var record in _assetSnapshot)
        {
            if (record.AssetPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                record.AssetType.Contains(search, StringComparison.OrdinalIgnoreCase))
                matches.Add(record);
        }
        return matches;
    }

    private static AssetRecord[] CreateSortedSnapshot(IEnumerable<AssetRecord> records) =>
        records.OrderBy(static record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static record => record.LocalIdentifier)
            .ToArray();

    private List<AssetRecord> ScanAssets(
        Action<AssetScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var paths = _workspace.AssetRoots.SelectMany(root =>
                _fileSystem.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Concat(_fileSystem.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => !ProjectAssetPathFilter.IsIgnored(path))))
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
            try { records.Add(Import(path, _fileSystem.DirectoryExists(path), cancellationToken)); }
            catch (IOException) when (!_fileSystem.FileExists(path) && !_fileSystem.DirectoryExists(path))
            {
                // An external rename/delete can race the directory snapshot. The watcher
                // schedules another refresh; a vanished source must not abort this scan.
            }
            BEngine.Editor.EditorCallbackDispatcher.Invoke(progress,
                new AssetScanProgress(index + 1, paths.Length, ToProjectPath(path)),
                "AssetDatabase.scanProgress");
        }
        AppendGeneratedArtifacts(records, cancellationToken);
        return records;
    }

    private void AppendGeneratedArtifacts(
        ICollection<AssetRecord> records,
        CancellationToken cancellationToken,
        Guid? ownerFilter = null)
    {
        if (!_fileSystem.DirectoryExists(_workspace.AssetArtifactsPath)) return;
        var scanRoot = ownerFilter.HasValue
            ? _pathService.Combine(_workspace.AssetArtifactsPath, ownerFilter.Value.ToString("N")[..2])
            : _workspace.AssetArtifactsPath;
        if (!_fileSystem.DirectoryExists(scanRoot)) return;
        var owners = records.Where(record => !record.IsSubAsset)
            .ToDictionary(record => record.Guid);
        var subAssetIdentities = records.Where(static record => record.ParentGuid.HasValue)
            .Select(static record => (record.ParentGuid!.Value, record.LocalIdentifier))
            .ToHashSet();
        foreach (var metaPath in _fileSystem.EnumerateFiles(
                     scanRoot, "*.meta", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetMetaDocument meta;
            try { meta = LoadYaml<AssetMetaDocument>(metaPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               InvalidDataException or FormatException or
                                               YamlDotNet.Core.YamlException)
            {
                BEngine.Debug.LogWarning($"Ignoring invalid generated artifact metadata '{metaPath}': " +
                                         exception.Message);
                continue;
            }

            if (!Guid.TryParse(meta.Guid, out var guid) ||
                !Guid.TryParse(meta.ParentGuid, out var parentGuid) ||
                meta.LocalIdentifier <= 0 ||
                ownerFilter.HasValue && parentGuid != ownerFilter.Value ||
                !owners.TryGetValue(parentGuid, out var owner)) continue;
            var artifactPath = metaPath[..^".meta".Length];
            if (!_fileSystem.FileExists(artifactPath)) continue;
            if (!subAssetIdentities.Add((parentGuid, meta.LocalIdentifier)))
                throw new InvalidDataException(
                    $"Generated sub-asset identity {parentGuid:N}/{meta.LocalIdentifier} is duplicated.");
            var hash = ComputeHash(artifactPath);
            var assetType = string.IsNullOrWhiteSpace(meta.AssetType)
                ? ResolveAssetType(artifactPath, isDirectory: false)
                : meta.AssetType;
            records.Add(new AssetRecord(guid, owner.AssetPath, artifactPath, metaPath,
                artifactPath, assetType, hash, false, parentGuid, meta.LocalIdentifier)
            {
                ArtifactHash = hash,
                ArtifactSize = _fileSystem.GetFileLength(artifactPath)
            });
        }
    }

    private AssetRecord Import(
        string sourcePath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metaPath = sourcePath + ".meta";
        var meta = LoadOrCreateMeta(metaPath, sourcePath, isDirectory, cancellationToken);
        meta.Settings ??= [];
        var guid = Guid.Parse(meta.Guid);
        var assetPath = ToProjectPath(sourcePath);
        var hash = isDirectory ? string.Empty : ComputeHash(sourcePath);
        var resolvedAssetType = ResolveAssetType(sourcePath, isDirectory);
        var importer = isDirectory
            ? null
            : AssetImporter.CreateForImport(assetPath, sourcePath, meta.Settings);
        var resolvedImporter = importer?.GetType().Name ?? "FolderImporter";
        var importFingerprint = importer is null
            ? string.Empty
            : ComputeImportFingerprint(hash, importer, sourcePath, meta.Settings);
        var artifactDirectory = _pathService.Combine(_workspace.AssetArtifactsPath, meta.Guid[..2]);
        _fileSystem.CreateDirectory(artifactDirectory);
        var extension = isDirectory ? ".folder" : _pathService.GetExtension(sourcePath);
        var artifactPath = _pathService.Combine(artifactDirectory, meta.Guid + extension);

        if (importer is not null &&
            (!_fileSystem.FileExists(artifactPath) ||
             !string.Equals(importFingerprint, meta.ImportFingerprint, StringComparison.Ordinal)))
        {
            using var artifactLock = AcquireWriteLock(artifactPath + ".lock", cancellationToken);
            if (!_fileSystem.FileExists(artifactPath) ||
                !string.Equals(importFingerprint, meta.ImportFingerprint, StringComparison.Ordinal))
            {
                using var context = new AssetImportContext(
                    assetPath, sourcePath, artifactPath, meta.Settings, cancellationToken,
                    _fileSystem, _pathService);
                try { importer.Import(context); context.Commit(); }
                finally { _hashBatch?.Invalidate(); }
            }
        }

        // Importers may normalize their source document while producing the Artifact.
        // Persist the post-import fingerprint so the next scan does not report a false update.
        if (!isDirectory)
        {
            hash = ComputeHash(sourcePath);
            importFingerprint = importer is null
                ? string.Empty
                : ComputeImportFingerprint(hash, importer, sourcePath, meta.Settings);
        }

        if (!string.Equals(meta.SourceHash, hash, StringComparison.Ordinal) ||
            !string.Equals(meta.AssetType, resolvedAssetType, StringComparison.Ordinal) ||
            !string.Equals(meta.Importer, resolvedImporter, StringComparison.Ordinal) ||
            !string.Equals(meta.ImportFingerprint, importFingerprint, StringComparison.Ordinal))
        {
            meta.SourceHash = hash;
            meta.AssetType = resolvedAssetType;
            meta.Importer = resolvedImporter;
            meta.ImportFingerprint = importFingerprint;
            SaveYaml(meta, metaPath);
        }

        var parentGuid = Guid.TryParse(meta.ParentGuid, out var parsedParent) ? parsedParent : (Guid?)null;
        // Folder assets have metadata but no importer artifact file.  Computing a hash for the
        // synthetic `.folder` path used to throw FileNotFoundException while creating a folder,
        // which escaped the editor command and terminated the native editor process.
        var artifactHash = isDirectory ? string.Empty : ComputeHash(artifactPath);
        var artifactSize = isDirectory ? 0 : _fileSystem.GetFileLength(artifactPath);
        return new AssetRecord(guid, assetPath, sourcePath, metaPath, artifactPath,
            meta.AssetType, hash, isDirectory, parentGuid, Math.Max(0, meta.LocalIdentifier))
        {
            ArtifactHash = artifactHash,
            ArtifactSize = artifactSize
        };
    }

    private AssetMetaDocument LoadOrCreateMeta(string metaPath, string sourcePath, bool isDirectory,
        CancellationToken cancellationToken)
    {
        using var writeLock = AcquireWriteLock(metaPath + ".lock", cancellationToken);
        if (_fileSystem.FileExists(metaPath))
        {
            try
            {
                var existing = LoadYaml<AssetMetaDocument>(metaPath);
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
        SaveYaml(document, metaPath);
        return document;
    }

    private Stream AcquireWriteLock(string path, CancellationToken cancellationToken)
    {
        var deadline = _platformService.TickCount64 + 15000;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return _fileSystem.OpenFile(
                    path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (_platformService.TickCount64 < deadline)
            {
                EditorHostServices.Wait.Wait(TimeSpan.FromMilliseconds(15));
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
            else if (!string.Equals(old.SourceHash, record.SourceHash, StringComparison.Ordinal) ||
                     !string.Equals(old.ArtifactHash, record.ArtifactHash, StringComparison.Ordinal))
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
        if (!_fileSystem.FileExists(_workspace.AssetDatabasePath)) return new AssetDatabaseDocument();
        try
        {
            return LoadYaml<AssetDatabaseDocument>(_workspace.AssetDatabasePath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            BEngine.Debug.LogWarning($"Asset database will be rebuilt: {exception.Message}");
            return new AssetDatabaseDocument();
        }
    }

    private void SaveManifest(IEnumerable<AssetRecord> records)
    {
        SaveYaml(new AssetDatabaseDocument
        {
            Assets = records.OrderBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Select(ToManifestEntry).ToList()
        }, _workspace.AssetDatabasePath);
    }

    private AssetDatabaseEntryDocument ToManifestEntry(AssetRecord record) => new()
    {
        Guid = record.Guid.ToString("N"),
        AssetPath = record.AssetPath,
        AssetType = record.AssetType,
        SourceHash = record.SourceHash,
        ArtifactPath = _pathService.GetRelativePath(
            _workspace.LibraryPath, record.ArtifactPath).Replace('\\', '/'),
        ArtifactHash = record.ArtifactHash,
        ArtifactSize = record.ArtifactSize,
        ParentGuid = record.ParentGuid?.ToString("N") ?? string.Empty,
        LocalIdentifier = record.LocalIdentifier
    };

    private void EnsureInsideAssets(string path)
    {
        if (!_workspace.AssetRoots.Any(root => path.StartsWith(
                root + _pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Asset path is outside the project Assets directory: {path}");
        }
    }

    private string ToProjectPath(string path) =>
        _pathService.GetRelativePath(_workspace.RootPath, path).Replace('\\', '/');
    private static string NormalizeAssetPath(string path) => path.Replace('\\', '/').TrimStart('/');
    private string ComputeHash(string path)
    {
        if (_hashBatch is not null) return _hashBatch.Compute(path);
        using var stream = _fileSystem.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private void CompleteHashBatch()
    {
        if (_hashBatch is not { } batch) return;
        _hashBatch = null; lastHashStatistics = batch.Statistics; batch.Dispose();
    }

    private string ComputeImportFingerprint(
        string sourceHash,
        AssetImporter importer,
        string sourcePath,
        IReadOnlyDictionary<string, string> settings)
    {
        var value = new StringBuilder();
        var importerType = importer.GetType();
        AppendFingerprintValue(value, sourceHash);
        foreach (var dependency in importer.GetSourceDependencies(sourcePath).Select(_pathService.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        { AppendFingerprintValue(value, dependency); AppendFingerprintValue(value, ComputeHash(dependency)); }
        AppendFingerprintValue(value, importerType.AssemblyQualifiedName ?? importerType.FullName ?? importerType.Name);
        if (Attribute.GetCustomAttribute(importerType, typeof(BEngine.Editor.AssetImporterVersionAttribute)) is
            BEngine.Editor.AssetImporterVersionAttribute version)
            AppendFingerprintValue(value, version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var setting in settings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendFingerprintValue(value, setting.Key);
            AppendFingerprintValue(value, setting.Value ?? string.Empty);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendFingerprintValue(StringBuilder target, string value) =>
        target.Append(value.Length).Append(':').Append(value).Append(';');

    private static string ResolveImporter(string path, bool isDirectory) => isDirectory
        ? "FolderImporter"
        : AssetImporter.ResolveImporterType(path).Name;

    private string ResolveAssetType(string path, bool isDirectory) => isDirectory ? "Folder" :
        path.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase) ? ResolveManagedAssetType(path) :
        BEngine.Editor.AssetTypeRegistry.Resolve(path) is { } extensionType ? extensionType :
        path.EndsWith(".prefab.yaml", StringComparison.OrdinalIgnoreCase) ? "Prefab" :
        path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase) ? "Scene" :
        path.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase) ? "UI Document" :
        path.EndsWith(".bss", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".bcss", StringComparison.OrdinalIgnoreCase) ? "UI Style Sheet" :
        path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ? "HTML Document" :
        path.EndsWith(".ui.yaml", StringComparison.OrdinalIgnoreCase) ? "Legacy UI Document" :
        path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase) ? "AssemblyDefinition" :
        _pathService.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "Script",
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" => "Texture",
            ".shader" or ".cg" => "Shader",
            ".json" => "JSON",
            ".xml" => "XML",
            ".md" => "Markdown",
            ".txt" => "Text",
            ".bpackage" => "BEnginePackage",
            ".yaml" => "YamlAsset",
            _ => "DefaultAsset"
        };

    private string ResolveManagedAssetType(string path)
    {
        try
        {
            var document = LoadYaml<ManagedAssetData>(path);
            if (document.Format == "BEngine.ManagedAsset" && !string.IsNullOrWhiteSpace(document.TypeName))
                return document.TypeName.Split(',')[0].Split('.').Last();
        }
        catch { }
        return "ManagedAsset";
    }

    private T LoadYaml<T>(string path) =>
        YamlUtility.Deserialize<T>(_fileSystem.ReadAllText(_pathService.GetFullPath(path)));

    private void SaveYaml(object value, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        var fullPath = _pathService.GetFullPath(path);
        var directory = _pathService.GetDirectoryName(fullPath) ??
                        throw new InvalidDataException($"Path has no directory: '{fullPath}'.");
        _fileSystem.CreateDirectory(directory);
        var temporaryPath = _pathService.Combine(
            directory, $".{_pathService.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            _fileSystem.WriteAllText(temporaryPath, YamlUtility.Serialize(value));
            _fileSystem.MoveFile(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath)) _fileSystem.DeleteFile(temporaryPath);
        }
    }
}
