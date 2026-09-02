namespace BEngine.Serialization;

public static class PrefabAssetOperations
{
    public static GameObject Instantiate(PrefabAsset prefab, Scene? destinationScene = null,
        Transform? parent = null, bool connectToPrefab = true)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        AssetDataValidation.ValidatePrefab(prefab.Data);
        var source = prefab.Data;
        var objectIds = CreateInstanceIdMap(source.GameObjects, preserveIds: false);
        var sceneDocument = new SceneAssetData
        {
            Id = Guid.NewGuid(),
            Name = source.Name,
            GameObjects = [.. source.GameObjects.Select(item =>
                CloneForInstance(item, source.Id, objectIds, connectToPrefab))]
        };
        using var temporary = SceneAssetSerialization.Restore(sceneDocument);
        var rootId = objectIds[source.Root];
        var root = temporary.Find(rootId) ?? throw new InvalidDataException("Prefab root could not be instantiated.");
        if (destinationScene is not null)
        {
            temporary.ReleaseAll();
            destinationScene.AddHierarchy(root);
        }
        else temporary.ReleaseAll();
        if (parent is not null) root.transform.SetParent(parent, false);
        return root;
    }

    public static GameObject LoadContents(PrefabAsset prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        AssetDataValidation.ValidatePrefab(prefab.Data);
        var ids = CreateInstanceIdMap(prefab.Data.GameObjects, preserveIds: true);
        var sceneDocument = new SceneAssetData
        {
            Id = Guid.NewGuid(),
            Name = prefab.name,
            GameObjects = [.. prefab.Data.GameObjects.Select(item =>
                CloneForInstance(item, prefab.assetId, ids, connected: false, preserveIds: true))]
        };
        using var temporary = SceneAssetSerialization.Restore(sceneDocument);
        var root = temporary.Find(prefab.Data.Root) ??
                   throw new InvalidDataException("Prefab root could not be loaded.");
        temporary.ReleaseAll();
        return root;
    }

    public static void ConnectHierarchy(GameObject root, Guid assetId)
    {
        foreach (var gameObject in Traverse(root))
        {
            gameObject.PrefabAssetId = assetId;
            gameObject.PrefabSourceId ??= gameObject.Id;
            gameObject.transform.PrefabAssetId = assetId;
            gameObject.transform.PrefabSourceId ??= gameObject.transform.Id;
            foreach (var component in gameObject.components.Where(item => item is not Transform))
            {
                component.PrefabAssetId = assetId;
                component.PrefabSourceId ??= component.Id;
            }
        }
    }

    public static void DisconnectHierarchy(GameObject root)
    {
        foreach (var gameObject in Traverse(root))
        {
            gameObject.PrefabAssetId = null;
            gameObject.PrefabSourceId = null;
            foreach (var component in gameObject.components)
            {
                component.PrefabAssetId = null;
                component.PrefabSourceId = null;
            }
        }
    }

    public static IEnumerable<GameObject> Traverse(GameObject root)
    {
        yield return root;
        foreach (var child in root.transform.children)
        foreach (var descendant in Traverse(child.gameObject))
            yield return descendant;
    }

    private static GameObjectData CloneForInstance(GameObjectData source, Guid assetId,
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
            Parent = source.Parent is { } parent ? objectIds[parent] : null,
            PrefabAsset = connected ? assetId : null,
            PrefabSource = connected ? source.Id : null,
            Transform = new TransformData
            {
                Id = objectIds[source.Transform.Id],
                Type = source.Transform.Type,
                PrefabAsset = connected ? assetId : null,
                PrefabSource = connected ? source.Transform.Id : null,
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
            PrefabAsset = connected ? assetId : null,
            PrefabSource = connected ? component.Id : null,
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

    private static ComponentGraphData? CloneGraph(
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
