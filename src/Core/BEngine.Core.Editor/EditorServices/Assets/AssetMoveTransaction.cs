namespace BEngine.Editor;

/// <summary>
/// A synchronous source move journal for asset move handlers. Calls the project
/// provider directly without redispatching handlers or modification processors.
/// Source edits are restored after moves are rolled back, before reimporting.
/// </summary>
public sealed class AssetMoveTransaction
{
    private readonly IEditorHost host;
    private readonly List<(string Source, string Destination)> moves = [];
    private readonly List<SourceSnapshot> sources = [];
    private readonly Dictionary<string, (string Full, string Project)> resolvedPaths = new(StringComparer.Ordinal);
    private bool active = true, changed;
    private AssetMoveTransaction(IEditorHost host) => this.host = host;

    /// <summary>Returns an empty string on success, or the failure and any rollback errors.</summary>
    public static string Execute(Action<AssetMoveTransaction> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (EditorBridge.Host is not { } host) return "No editor project is open.";
        var transaction = new AssetMoveTransaction(host);
        try
        {
            EditorAssetWritePolicy.EnsureCanWrite("Moving project assets");
            operation(transaction);
            return string.Empty;
        }
        catch (Exception exception)
        {
            var failures = transaction.Rollback();
            if (exception is not (IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or FormatException or ArgumentException))
            {
                if (failures.Count > 0)
                    throw new AggregateException("Asset move failed and could not be fully rolled back.",
                        [exception, .. failures.Select(message => new IOException(message))]);
                throw;
            }
            return failures.Count == 0 ? exception.Message :
                $"{exception.Message} Rollback errors: {string.Join("; ", failures)}";
        }
        finally
        {
            transaction.active = false;
            transaction.moves.Clear();
            transaction.sources.Clear();
            transaction.resolvedPaths.Clear();
            if (transaction.changed)
            {
                BAsset.ClearLoadedAssets();
                AssetDatabase.ClearAssetCache();
                EditorAssetAdapters.Invalidate();
            }
        }
    }

    public EditorAssetInfo? GetAsset(string path) => host.GetAsset(ProjectPath(path));

    public EditorAssetInfo? FindFileSubAsset(string ownerPath, long localIdentifier, string assetType)
    {
        var owner = GetAsset(ownerPath);
        if (owner is not { ParentGuid: null }) return null;
        foreach (var item in host.FindAssets(string.Empty))
            if (item.ParentGuid == owner.Value.Guid && item.LocalIdentifier == localIdentifier &&
                item.AssetType.Equals(assetType, StringComparison.OrdinalIgnoreCase)) return item;
        return null;
    }

    public bool IsGeneratedArtifact(EditorAssetInfo asset)
    {
        EnsureActive();
        return AssetDatabase.IsGeneratedArtifact(asset, host);
    }

    /// <summary>Resolves a source path inside the current project's Assets directory.</summary>
    public string GetSourcePath(string path)
    {
        return Resolve(path).Full;
    }

    public bool Exists(string path)
    {
        var full = GetSourcePath(path);
        return GetAsset(path) is not null || AssetDatabase.FileSystem.FileExists(full) ||
            AssetDatabase.FileSystem.DirectoryExists(full);
    }

    /// <summary>Moves a source and its metadata through the project provider.</summary>
    public void Move(string source, string destination)
    {
        source = ProjectPath(source);
        destination = ProjectPath(destination);
        EnsureWritable(source);
        EnsureWritable(destination);
        if (AssetDatabase.SameAssetPath(source, destination)) return;
        if (Exists(destination) || AssetDatabase.FileSystem.FileExists(GetSourcePath(destination) + ".meta"))
            throw new IOException($"Destination already exists: {destination}");
        var sourceExisted = SourceExists(source);
        changed = true;
        // Refresh can fail after a provider has already moved the source.
        try
        {
            var error = host.MoveAsset(source, destination);
            if (error.Length > 0) throw new IOException(error);
        }
        finally
        {
            if (sourceExisted && !SourceExists(source) && SourceExists(destination))
            {
                moves.Add((source, destination));
                RelocateSnapshots(source, destination);
            }
        }
    }

    /// <summary>Edits and imports a source, retaining its original bytes until commit.</summary>
    public void EditSource(string path, Action<string> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        path = ProjectPath(path);
        EnsureWritable(path);
        if (!sources.Any(item => AssetDatabase.SameAssetPath(item.Path, path)))
            sources.Add(new SourceSnapshot(path, AssetDatabase.FileSystem.ReadAllBytes(GetSourcePath(path))));
        changed = true;
        edit(GetSourcePath(path));
        host.ImportAsset(path);
    }

    private void EnsureActive()
    {
        if (!active || !ReferenceEquals(EditorBridge.Host, host))
            throw new InvalidOperationException("The asset move transaction is no longer active.");
    }

    private void EnsureWritable(string path)
    {
        EnsureActive();
        EditorAssetWritePolicy.EnsureCanWrite("Moving or editing project assets");
        if (AssetProtection.IsProtected(path))
            throw new InvalidOperationException("This asset is protected by its owning package and cannot be moved or replaced.");
    }

    private string ProjectPath(string path) => Resolve(path).Project;

    private (string Full, string Project) Resolve(string path)
    {
        EnsureActive();
        if (resolvedPaths.TryGetValue(path, out var resolved)) return resolved;
        var full = AssetDatabase.ResolveAssetPath(path);
        resolved = (full, AssetDatabase.Paths.GetRelativePath(host.ProjectRootPath, full).Replace('\\', '/'));
        resolvedPaths[path] = resolved;
        resolvedPaths[full] = resolved;
        resolvedPaths[resolved.Project] = resolved;
        return resolved;
    }

    private bool SourceExists(string path)
    {
        var full = GetSourcePath(path);
        return AssetDatabase.FileSystem.FileExists(full) || AssetDatabase.FileSystem.DirectoryExists(full);
    }

    private void RelocateSnapshots(string source, string destination)
    {
        foreach (var snapshot in sources)
            if (AssetDatabase.SameAssetPath(snapshot.Path, source)) snapshot.Path = destination;
            else if (snapshot.Path.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase))
                snapshot.Path = destination + snapshot.Path[source.Length..];
    }

    private List<string> Rollback()
    {
        var errors = new List<string>();
        if (!ReferenceEquals(EditorBridge.Host, host))
        {
            if (changed) errors.Add("The editor project changed before its asset transaction could be rolled back.");
            return errors;
        }
        for (var i = moves.Count - 1; i >= 0; i--)
        {
            var (source, destination) = moves[i];
            try
            {
                var error = host.MoveAsset(destination, source);
                if (error.Length > 0) errors.Add($"{destination} -> {source}: {error}");
            }
            catch (Exception exception) { errors.Add($"{destination} -> {source}: {exception.Message}"); }
            finally
            {
                if (!SourceExists(destination) && SourceExists(source)) RelocateSnapshots(destination, source);
            }
        }
        // Restore all sources before any importer reads their companion files.
        var restored = new List<string>();
        foreach (var snapshot in sources)
            try
            {
                AssetDatabase.FileSystem.WriteAllBytes(GetSourcePath(snapshot.Path), snapshot.Bytes);
                restored.Add(snapshot.Path);
            }
            catch (Exception exception) { errors.Add($"Restoring {snapshot.Path}: {exception.Message}"); }
        foreach (var path in restored)
            try { host.ImportAsset(path); }
            catch (Exception exception) { errors.Add($"Reimporting {path}: {exception.Message}"); }
        return errors;
    }

    private sealed class SourceSnapshot(string path, byte[] bytes)
    {
        internal string Path = path;
        internal readonly byte[] Bytes = bytes;
    }
}
