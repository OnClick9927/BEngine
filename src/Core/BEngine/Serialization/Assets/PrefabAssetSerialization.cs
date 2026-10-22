using BEngine.Serialization;

namespace BEngine.Serialization;

internal static class PrefabAssetSerialization
{
    internal static PrefabAsset Load(string path) => Document<PrefabAsset>
        .Read(path, sourcePath => CreateAsset(YamlUtility.Load<PrefabAssetData>(sourcePath), sourcePath))
        .ToAsset();

    internal static PrefabAsset Deserialize(string yaml, string sourcePath = "") => Document<PrefabAsset>
        .Parse(yaml, contents => CreateAsset(YamlUtility.Deserialize<PrefabAssetData>(contents), sourcePath))
        .ToAsset();

    internal static string Serialize(PrefabAsset prefab) => Document<PrefabAsset>.FromAsset(prefab)
        .Serialize(static asset => YamlUtility.Serialize(asset.Data));

    internal static string Serialize(GameObject root, Guid? assetId = null) =>
        YamlUtility.Serialize(Capture(root, assetId));

    internal static void Save(PrefabAsset prefab, string path) => Document<PrefabAsset>.FromAsset(prefab)
        .Write(path, static (asset, destination) => YamlUtility.Save(asset.Data, destination));

    internal static PrefabAsset Save(GameObject root, string path, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var existing = RuntimeServiceAccess.FileSystem.FileExists(path) ? YamlUtility.Load<PrefabAssetData>(path) : null;
        var data = Capture(root, assetId ?? existing?.Id ?? Guid.NewGuid());
        if (existing?.BasePrefab is { } inherited)
        {
            data.BasePrefab = PrefabMerge.Clone(inherited);
            var source = PrefabResolver.Resolve(PrefabResolver.Load(inherited.Asset));
            data.BasePrefab.Baseline = PrefabResolver.Map(source, data.BasePrefab, connected: false).GameObjects;
        }
        var prefab = CreateAsset(data, path);
        if (root.IsRuntimeOnly || root.SceneUnchecked is { } scene &&
            (scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene)))
            prefab.IsRuntimeOnly = true;
        Save(prefab, path);
        BAsset.Invalidate(path);
        return prefab;
    }

    internal static PrefabAssetData Capture(GameObject root, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var hierarchy = PrefabAssetOperations.Traverse(root).ToArray();
        var objectIds = CaptureIdMap(root);
        var document = new PrefabAssetData
        {
            Id = assetId ?? root.PrefabAssetId ?? Guid.NewGuid(), Name = root.name, Root = objectIds[root.Id]
        };
        foreach (var gameObject in hierarchy)
        {
            var item = PrefabAssetOperations.Remap(SceneAssetSerialization.FromGameObject(gameObject), objectIds);
            if (ReferenceEquals(gameObject, root)) item.Parent = null;
            ClearLink(item, root.PrefabAssetId);
            document.GameObjects.Add(item);
            if (!ReferenceEquals(gameObject, root) && gameObject.PrefabInstance is { } reference)
                document.Instances.Add(PrefabResolver.RemapReference(reference, objectIds));
        }
        var localIds = PrefabResolver.Ids(document.GameObjects).ToHashSet();
        foreach (var item in document.GameObjects)
        foreach (var graph in item.Components.Select(component => component.Graph).Prepend(item.Transform.Graph))
            if (graph is not null)
                foreach (var member in graph.Members) ValidateReference(member.Value);
        return document;

        void ValidateReference(SerializedValueData value)
        {
            if (value.Kind == "EngineObject" && value.Value.StartsWith("scene:", StringComparison.Ordinal) &&
                Guid.TryParse(value.Value.AsSpan(6), out var id) && !localIds.Contains(id))
                throw new InvalidDataException("Prefab assets cannot reference scene objects outside their hierarchy.");
            foreach (var child in value.Members) ValidateReference(child.Value);
            foreach (var child in value.Items) ValidateReference(child);
            foreach (var entry in value.Entries) { ValidateReference(entry.Key); ValidateReference(entry.Value); }
        }
    }

    internal static Dictionary<Guid, Guid> CaptureIdMap(GameObject root)
    {
        var sourceIds = root.PrefabInstance?.ObjectIds.ToDictionary(pair => pair.Value, pair => pair.Key);
        return PrefabAssetOperations.Traverse(root).SelectMany(item => item.components.Cast<BObject>().Prepend(item))
            .ToDictionary(item => item.Id, item => sourceIds?.GetValueOrDefault(item.Id, item.Id) ??
                (item.PrefabAssetId == root.PrefabAssetId ? item.PrefabSourceId : null) ?? item.Id);
    }

    internal static PrefabAsset SaveVariant(GameObject root, PrefabAsset basePrefab, string path, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(basePrefab);
        if (root.IsRuntimeOnly || root.scene?.IsRuntimeOnly == true || root.scene is { } scene && SceneRuntime.IsRunningScene(scene))
            throw new InvalidOperationException("Runtime prefab instances cannot be saved as variants.");
        var data = Capture(root, assetId ?? TryReadAssetId(path) ?? Guid.NewGuid());
        if (data.Id == basePrefab.assetId) throw new InvalidOperationException("A variant cannot replace its base prefab.");
        var source = PrefabResolver.Resolve(basePrefab);
        var captureIds = CaptureIdMap(root);
        var linkedIds = root.PrefabInstance?.ObjectIds ?? throw new InvalidOperationException("Create a variant from a connected prefab instance or loaded prefab contents.");
        data.BasePrefab = new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(basePrefab), Root = data.Root,
            ObjectIds = linkedIds.ToDictionary(pair => pair.Key, pair => captureIds.GetValueOrDefault(pair.Value, pair.Value))
        };
        data.BasePrefab.Baseline = PrefabResolver.Map(source, data.BasePrefab, connected: false).GameObjects;
        var prefab = CreateAsset(data, path);
        Save(prefab, path); BAsset.Invalidate(path);
        return prefab;
    }

    private static void ClearLink(GameObjectData item, Guid? outerAsset)
    {
        if (item.PrefabAsset == outerAsset) { item.PrefabAsset = null; item.PrefabSource = null; }
        if (item.Transform.PrefabAsset == outerAsset) { item.Transform.PrefabAsset = null; item.Transform.PrefabSource = null; }
        foreach (var component in item.Components)
            if (component.PrefabAsset == outerAsset) { component.PrefabAsset = null; component.PrefabSource = null; }
    }

    internal static PrefabAsset CreateAsset(PrefabAssetData document, string sourcePath)
    {
        AssetDataValidation.ValidatePrefab(document);
        return new PrefabAsset(document, sourcePath) { Id = document.Id, name = document.Name };
    }

    private static Guid? TryReadAssetId(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !RuntimeServiceAccess.FileSystem.FileExists(path)) return null;
        try { return YamlUtility.Load<PrefabAssetData>(path).Id; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                          YamlDotNet.Core.YamlException) { return null; }
    }
}
