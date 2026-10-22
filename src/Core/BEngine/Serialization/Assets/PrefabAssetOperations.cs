namespace BEngine.Serialization;

public static class PrefabAssetOperations
{
    public static GameObject Instantiate(PrefabAsset prefab, Scene? destinationScene = null,
        Transform? parent = null, bool connectToPrefab = true)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        var source = PrefabResolver.Resolve(prefab);
        var objectIds = CreateInstanceIdMap(source.GameObjects, preserveIds: false);
        var reference = new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(prefab), Root = objectIds[source.Root], ObjectIds = objectIds
        };
        var mapped = PrefabResolver.Map(source, reference, connectToPrefab);
        reference.Baseline = PrefabMerge.Clone(mapped.GameObjects);
        var sceneDocument = new SceneAssetData
        {
            Id = Guid.NewGuid(), Name = source.Name, GameObjects = mapped.GameObjects,
            PrefabInstances = mapped.Instances
        };
        if (connectToPrefab) sceneDocument.PrefabInstances.Insert(0, reference);
        using var temporary = SceneAssetSerialization.Restore(sceneDocument, resolvePrefabs: false);
        var root = temporary.Find(reference.Root) ?? throw new InvalidDataException("Prefab root could not be instantiated.");
        temporary.ReleaseAll();
        destinationScene?.AddHierarchy(root);
        if (parent is not null) root.transform.SetParent(parent, false);
        return root;
    }

    public static GameObject LoadContents(PrefabAsset prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        var source = PrefabResolver.Resolve(prefab);
        var ids = CreateInstanceIdMap(source.GameObjects, preserveIds: true);
        var sceneDocument = new SceneAssetData
        {
            Id = Guid.NewGuid(), Name = prefab.name,
            GameObjects = source.GameObjects.Select(item => CloneForInstance(item, prefab.assetId, ids, false)).ToList(),
            PrefabInstances = source.Instances
        };
        using var temporary = SceneAssetSerialization.Restore(sceneDocument, resolvePrefabs: false);
        var root = temporary.Find(source.Root) ?? throw new InvalidDataException("Prefab root could not be loaded.");
        temporary.ReleaseAll();
        // Keep authoring provenance without presenting the contents as a scene instance.
        root.PrefabInstance = new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(prefab), Root = root.Id, ObjectIds = ids,
            Baseline = PrefabMerge.Clone(source.GameObjects)
        };
        return root;
    }

    public static void ConnectHierarchy(GameObject root, Guid assetId) =>
        ConnectHierarchy(root, BAsset.LoadByGuid<PrefabAsset>(assetId.ToString("N")) ??
            throw new InvalidDataException($"Cannot connect to missing prefab {assetId}."));

    internal static void ConnectHierarchy(GameObject root, PrefabAsset asset)
    {
        var ids = PrefabAssetSerialization.CaptureIdMap(root).ToDictionary(pair => pair.Value, pair => pair.Key);
        var source = PrefabResolver.Resolve(asset);
        var reference = new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(asset), Root = root.Id, ObjectIds = ids
        };
        var mapped = PrefabResolver.Map(source, reference, connected: true);
        reference.Baseline = PrefabMerge.Clone(mapped.GameObjects);
        var live = Traverse(root).SelectMany(item => item.components.Cast<BObject>().Prepend(item)).ToDictionary(item => item.Id);
        foreach (var item in mapped.GameObjects)
        {
            Link(item.Id, item.PrefabAsset, item.PrefabSource);
            Link(item.Transform.Id, item.Transform.PrefabAsset, item.Transform.PrefabSource);
            foreach (var component in item.Components) Link(component.Id, component.PrefabAsset, component.PrefabSource);
        }
        root.PrefabInstance = reference;
        void Link(Guid id, Guid? prefab, Guid? sourceId)
        {
            if (!live.TryGetValue(id, out var value)) return;
            value.PrefabAssetId = prefab; value.PrefabSourceId = sourceId;
        }
    }

    public static void DisconnectHierarchy(GameObject root, bool completely = true)
    {
        var asset = root.PrefabAssetId;
        foreach (var gameObject in Traverse(root))
        {
            if (completely || ReferenceEquals(gameObject, root)) gameObject.PrefabInstance = null;
            foreach (var item in gameObject.components.Cast<BObject>().Prepend(gameObject))
                if (completely || item.PrefabAssetId == asset)
                { item.PrefabAssetId = null; item.PrefabSourceId = null; }
        }
    }

    public static IEnumerable<GameObject> Traverse(GameObject root)
    {
        yield return root;
        foreach (var child in root.transform.children)
        foreach (var descendant in Traverse(child.gameObject))
            yield return descendant;
    }

    internal static GameObjectData CloneForInstance(GameObjectData source, Guid assetId,
        IReadOnlyDictionary<Guid, Guid> objectIds, bool connected, bool preserveIds = false)
    {
        var instance = new GameObjectData
        {
            Id = objectIds[source.Id],
            Name = source.Name,
            Active = source.Active,
            Tag = source.Tag,
            Layer = source.Layer,
            IsStatic = source.IsStatic,
            Parent = source.Parent is { } parent ? objectIds.GetValueOrDefault(parent, parent) : null,
            PrefabAsset = source.PrefabAsset ?? (connected ? assetId : null),
            PrefabSource = source.PrefabSource ?? (connected ? source.Id : null),
            Transform = new TransformData
            {
                Id = objectIds[source.Transform.Id],
                Type = source.Transform.Type,
                PrefabAsset = source.Transform.PrefabAsset ?? (connected ? assetId : null),
                PrefabSource = source.Transform.PrefabSource ?? (connected ? source.Transform.Id : null),
                LocalPosition = new FixedVector2Data(source.Transform.LocalPosition.ToVector2()),
                LocalRotation = source.Transform.LocalRotation,
                LocalScale = new FixedVector2Data(source.Transform.LocalScale.ToVector2()),
                Fields = new Dictionary<string, string>(source.Transform.Fields, StringComparer.Ordinal),
                Graph = CloneGraph(source.Transform.Graph, objectIds)
            }
        };
        instance.Components.AddRange(source.Components.Select(component => new ComponentData
        {
            Id = objectIds[component.Id],
            Type = component.Type,
            Enabled = component.Enabled,
            PrefabAsset = component.PrefabAsset ?? (connected ? assetId : null),
            PrefabSource = component.PrefabSource ?? (connected ? component.Id : null),
            Fields = new Dictionary<string, string>(component.Fields, StringComparer.Ordinal),
            Graph = CloneGraph(component.Graph, objectIds)
        }));
        return instance;
    }

    private static Dictionary<Guid, Guid> CreateInstanceIdMap(
        IEnumerable<GameObjectData> gameObjects,
        bool preserveIds)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var gameObject in gameObjects)
        {
            result.Add(gameObject.Id, preserveIds ? gameObject.Id : Guid.NewGuid());
            result.Add(gameObject.Transform.Id, preserveIds ? gameObject.Transform.Id : Guid.NewGuid());
            foreach (var component in gameObject.Components)
                result.Add(component.Id, preserveIds ? component.Id : Guid.NewGuid());
        }
        return result;
    }

    internal static GameObjectData Remap(GameObjectData source, IReadOnlyDictionary<Guid, Guid> ids)
    {
        var complete = PrefabResolver.Ids([source]).ToDictionary(id => id, id => ids.GetValueOrDefault(id, id));
        foreach (var pair in ids) complete[pair.Key] = pair.Value;
        return CloneForInstance(source, Guid.Empty, complete, false);
    }

    internal static ComponentGraphData? CloneGraph(
        ComponentGraphData? graph,
        IReadOnlyDictionary<Guid, Guid> objectIds)
    {
        if (graph is null) return null;
        var clone = ComponentObjectGraphSerializer.Clone(graph);
        foreach (var member in clone.Members) Remap(member.Value);
        return clone;

        void Remap(SerializedValueData value)
        {
            const string scenePrefix = "scene:";
            if (value.Kind == "EngineObject" && value.Value.StartsWith(scenePrefix, StringComparison.Ordinal) &&
                Guid.TryParse(value.Value.AsSpan(scenePrefix.Length), out var sourceId) &&
                objectIds.TryGetValue(sourceId, out var destinationId))
                value.Value = $"{scenePrefix}{destinationId:D}";
            foreach (var item in value.Items) Remap(item);
            foreach (var child in value.Members) Remap(child.Value);
            foreach (var entry in value.Entries)
            {
                Remap(entry.Key);
                Remap(entry.Value);
            }
        }
    }
}
