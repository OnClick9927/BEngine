namespace BEngine.Serialization;

/// <summary>A detached scene document for authoring tools. Parsing and editing never creates
/// live objects, resolves prefabs, or executes component serialization callbacks.</summary>
public readonly struct SceneDocumentSnapshot
{
    private readonly SceneAssetData? _document;
    private SceneAssetData Data => _document ?? throw new InvalidOperationException("The scene document is uninitialized.");
    private SceneDocumentSnapshot(SceneAssetData document) => _document = document;
    public Guid Id => Data.Id;
    public string Name => Data.Name;
    public SerializedHierarchy Objects => new(Data.GameObjects);
    public PrefabReferenceSet References => new(Data.PrefabInstances);
    public string ToYaml() => YamlUtility.Serialize(Data);
    public static SceneDocumentSnapshot Parse(string yaml) => new(YamlUtility.Deserialize<SceneAssetData>(yaml));
}
