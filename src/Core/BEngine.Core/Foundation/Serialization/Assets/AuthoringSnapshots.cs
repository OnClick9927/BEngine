namespace BEngine.Serialization;

/// <summary>A detached prefab document. Source inheritance policy operates on this handle, never asset storage.</summary>
public readonly struct PrefabDocumentSnapshot
{
    private readonly PrefabAssetData? _document;
    private readonly bool _transient;
    internal PrefabAssetData Data => _document ?? throw new InvalidOperationException("The prefab document is uninitialized.");
    internal PrefabDocumentSnapshot(PrefabAssetData document, bool transient = false) { _document = document; _transient = transient; }
    public Guid Id => Data.Id;
    public Guid Root => Data.Root;
    public SerializedHierarchy Objects => new(Data.GameObjects);
    public PrefabReferenceSet References => new(Data.Instances);
    public PrefabReferenceSnapshot? BaseReference => Data.BasePrefab is { } reference ? new(reference) : null;
    public void SetObjects(SerializedHierarchy objects) => Data.GameObjects = objects.Data;
    public void SetBase(PrefabReferenceSnapshot reference) => Data.BasePrefab = reference.Data;
    public void Validate() => AssetDataValidation.ValidatePrefab(Data);
    public string ToYaml() => YamlUtility.Serialize(Data);
    public static string SerializeAsset(PrefabAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        return YamlUtility.Serialize(asset.Data);
    }
    public PrefabAsset CreateAsset(string sourcePath)
    {
        // An editing handle may outlive this call. Its subsequent edits must not
        // alter an already-published asset (including reference identity maps).
        var asset = PrefabAssetSerialization.CreateAsset(AssetSnapshotClone.Clone(Data), sourcePath);
        asset.IsRuntimeOnly = _transient;
        return asset;
    }
    public static PrefabDocumentSnapshot Parse(string yaml) => new(YamlUtility.Deserialize<PrefabAssetData>(yaml));
    /// <summary>Restores a freshly parsed document without creating a retained editing handle.</summary>
    public static PrefabAsset ParseAsset(string yaml, string sourcePath = "") =>
        PrefabAssetSerialization.CreateAsset(YamlUtility.Deserialize<PrefabAssetData>(yaml), sourcePath);
    public static PrefabAsset LoadSource(string path) => PrefabAssetSerialization.Load(path);
    public static PrefabDocumentSnapshot Capture(GameObject root, Guid? assetId = null) =>
        new(PrefabAssetSerialization.Capture(root, assetId),
            root.IsRuntimeOnly || root.SceneUnchecked is { } scene && (scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene)));
    public static PrefabDocumentSnapshot CopyFrom(PrefabAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        return new(AssetSnapshotClone.Clone(asset.Data), asset.IsRuntimeOnly);
    }
    public static PrefabDocumentSnapshot Resolve(PrefabAsset asset) => new(PrefabResolver.Resolve(asset));
    public static PrefabDocumentSnapshot Map(PrefabDocumentSnapshot source, PrefabReferenceSnapshot reference, bool connected) =>
        new(PrefabResolver.Map(source.Data, reference.Data, connected));
    public static Dictionary<Guid, Guid> CaptureIdMap(GameObject root) => PrefabAssetSerialization.CaptureIdMap(root);
    public static Dictionary<Guid, Guid> CaptureConnectionMap(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.PrefabInstance is { } instance ? new(instance.ObjectIds) :
            throw new InvalidOperationException("Create a variant from a connected prefab instance or loaded prefab contents.");
    }
}

/// <summary>Object capture/restore and encoding, without source-file I/O or document policy.</summary>
public static class SceneAssetDocuments
{
    public static Scene Parse(string yaml, IServiceProvider? services = null) =>
        SceneAssetSerialization.Restore(YamlUtility.Deserialize<SceneAssetData>(yaml), services);
    public static string Serialize(Scene scene) =>
        YamlUtility.Serialize(SceneAssetSerialization.Capture(scene, allowTransientObjects: false));
}

/// <summary>A source-content policy, registered through the generic service mechanism.</summary>
public interface IPrefabResolutionBackend
{
    PrefabDocumentSnapshot Resolve(PrefabAsset asset);
    PrefabAsset Load(string reference);
    string Reference(PrefabAsset asset);
    void ResolveReferences(SerializedHierarchy objects, PrefabReferenceSet references);
    SerializedHierarchy Merge(SerializedHierarchy baseline, SerializedHierarchy current, SerializedHierarchy source);
}
