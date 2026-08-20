namespace BEngine.Documents;

internal sealed class PrefabDocumentConverter : IDocumentConverter
{
    private static readonly Type[] SupportedObjectTypes = [typeof(PrefabAsset), typeof(GameObject)];

    internal static PrefabDocumentConverter Shared { get; } = new();

    public Type DocumentType => typeof(PrefabDocument);
    public IReadOnlyList<Type> ObjectTypes => SupportedObjectTypes;

    public BObject ToBObject(Document document, DocumentConversionContext context) =>
        CreateAsset((PrefabDocument)document, context.SourcePath);

    public Document FromBObject(BObject value, DocumentConversionContext context) => value switch
    {
        PrefabAsset prefab => prefab.Document,
        GameObject gameObject => FromGameObject(gameObject, TryReadAssetId(context.SourcePath)),
        _ => throw new NotSupportedException($"Cannot create a PrefabDocument from {value.GetType().FullName}.")
    };

    internal static PrefabDocument FromGameObject(GameObject root, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var hierarchy = PrefabDocumentOperations.Traverse(root).ToArray();
        var objectIds = hierarchy.ToDictionary(item => item.Id, item => item.PrefabSourceId ?? item.Id);
        var document = new PrefabDocument
        {
            Id = assetId ?? root.PrefabAssetId ?? Guid.NewGuid(),
            Name = root.name,
            Root = objectIds[root.Id]
        };

        foreach (var gameObject in hierarchy)
        {
            var item = SceneDocumentConverter.FromGameObject(gameObject);
            item.Id = objectIds[gameObject.Id];
            item.Parent = gameObject.transform.parent is { } parent &&
                          objectIds.TryGetValue(parent.gameObject.Id, out var parentId) ? parentId : null;
            item.PrefabAsset = null;
            item.PrefabSource = null;
            item.Transform.Id = gameObject.transform.PrefabSourceId ?? gameObject.transform.Id;
            item.Transform.PrefabAsset = null;
            item.Transform.PrefabSource = null;
            foreach (var component in item.Components)
            {
                var live = gameObject.components.First(candidate => candidate.Id == component.Id);
                component.Id = live.PrefabSourceId ?? live.Id;
                component.PrefabAsset = null;
                component.PrefabSource = null;
            }
            document.GameObjects.Add(item);
        }

        return document;
    }

    internal static PrefabAsset CreateAsset(PrefabDocument document, string sourcePath)
    {
        CoreDocumentRegistration.ValidatePrefab(document);
        return new PrefabAsset(document, sourcePath) { Id = document.Id, name = document.Name };
    }

    private static Guid? TryReadAssetId(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return Document.Load<PrefabDocument>(path).Id; }
        catch (Exception exception) when (exception is IOException or InvalidDataException) { return null; }
    }
}
