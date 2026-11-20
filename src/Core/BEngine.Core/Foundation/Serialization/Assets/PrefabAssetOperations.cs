namespace BEngine.Serialization;

public static class PrefabAssetOperations
{
    /// <summary>Reads an object's source identity without exposing mutable prefab metadata.
    /// Non-hierarchy objects have no source identity.</summary>
    public static (Guid? Asset, Guid? Source) GetSourceIdentity(BObject? value) => value switch
    {
        GameObject gameObject => (gameObject.PrefabAssetId, gameObject.PrefabSourceId),
        Component component => (component.PrefabAssetId, component.PrefabSourceId),
        _ => (null, null)
    };

    /// <summary>Tests the stored instance boundary, including nested instances of the same asset.</summary>
    public static bool IsInstanceRoot(GameObject? gameObject) => gameObject is not null &&
        gameObject.PrefabAssetId.HasValue && (gameObject.PrefabInstance is not null || gameObject.transform.parent is null ||
        gameObject.transform.parent.gameObject.PrefabAssetId != gameObject.PrefabAssetId);

    /// <summary>Reads this object's instance reference. A child without its own reference returns null.
    /// Resolving the address or finding an ancestor is the caller's policy.</summary>
    public static string? GetInstanceReference(GameObject? gameObject) => gameObject?.PrefabInstance?.Asset;

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
        reference.Baseline = AssetSnapshotClone.Clone(mapped.GameObjects);
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
            Id = Guid.NewGuid(),
            Name = prefab.name,
            GameObjects = ProjectItems1871(),
            PrefabInstances = source.Instances
        };
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData> ProjectItems1871()
        {
            var projectionSource1871 = source.GameObjects;
            ArgumentNullException.ThrowIfNull(projectionSource1871, "source");
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> projectionValues1871 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource1871);
            var projectionOutput1871 = new global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>(projectionValues1871.Length);
            global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(projectionOutput1871, projectionValues1871.Length);
            var projectionTarget1871 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionOutput1871);
            var projectionIndex1871 = 0;
            foreach (var item in projectionValues1871)
            {
                projectionTarget1871[projectionIndex1871++] = CloneForInstance(item, prefab.assetId, ids, false);
            }
        
            return projectionOutput1871;
        }
        using var temporary = SceneAssetSerialization.Restore(sceneDocument, resolvePrefabs: false);
        var root = temporary.Find(source.Root) ?? throw new InvalidDataException("Prefab root could not be loaded.");
        temporary.ReleaseAll();
        // Keep authoring provenance without presenting the contents as a scene instance.
        root.PrefabInstance = new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(prefab), Root = root.Id, ObjectIds = ids,
            Baseline = AssetSnapshotClone.Clone(source.GameObjects)
        };
        return root;
    }

    public static void ConnectHierarchy(GameObject root, Guid assetId) =>
        ConnectHierarchy(root, BAsset.LoadByGuid<PrefabAsset>(assetId.ToString("N")) ??
            throw new InvalidDataException($"Cannot connect to missing prefab {assetId}."));

    /// <summary>Connects a live hierarchy to an already resolved asset, preserving its live identities.
    /// File persistence, import, Undo and editor notifications belong to the caller.</summary>
    public static void ConnectHierarchy(GameObject root, PrefabAsset asset)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(root.isDestroyed, root);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        var ids = BuildIndex4107(PrefabAssetSerialization.CaptureIdMap(root), null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid> BuildIndex4107(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<global::System.Guid, global::System.Guid>> indexSource4107, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer4107)
        {
            ArgumentNullException.ThrowIfNull(indexSource4107, "source");
            var knownCount = indexSource4107.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(indexComparer4107);
            global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<global::System.Guid, global::System.Guid>> values;
            if (knownCount && indexSource4107 is global::System.Collections.Generic.KeyValuePair<global::System.Guid, global::System.Guid>[] array)
                values = array;
            else if (knownCount && indexSource4107 is global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<global::System.Guid, global::System.Guid>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(capacity, indexComparer4107);
                foreach (var item in indexSource4107)
                    result.Add(IndexKey4107(item), IndexValue4107(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(values.Length, indexComparer4107);
                foreach (var item in values)
                    result.Add(IndexKey4107(item), IndexValue4107(item));
                return result;
            }
        
            global::System.Guid IndexKey4107(global::System.Collections.Generic.KeyValuePair<global::System.Guid, global::System.Guid> pair) => pair.Value;
            global::System.Guid IndexValue4107(global::System.Collections.Generic.KeyValuePair<global::System.Guid, global::System.Guid> pair) => pair.Key;
        }
        var source = PrefabResolver.Resolve(asset);
        var reference = new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(asset), Root = root.Id, ObjectIds = ids
        };
        var mapped = PrefabResolver.Map(source, reference, connected: true);
        reference.Baseline = AssetSnapshotClone.Clone(mapped.GameObjects);
        var live = BuildIndex4588(Traverse(root).SelectMany(item => item.components.Cast<BObject>().Prepend(item)), null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.BObject> BuildIndex4588(global::System.Collections.Generic.IEnumerable<global::BEngine.BObject> indexSource4588, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer4588)
        {
            ArgumentNullException.ThrowIfNull(indexSource4588, "source");
            var knownCount = indexSource4588.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.BObject>(indexComparer4588);
            global::System.ReadOnlySpan<global::BEngine.BObject> values;
            if (knownCount && indexSource4588 is global::BEngine.BObject[] array)
                values = array;
            else if (knownCount && indexSource4588 is global::System.Collections.Generic.List<global::BEngine.BObject> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.BObject>(capacity, indexComparer4588);
                foreach (var item in indexSource4588)
                    result.Add(IndexKey4588(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.BObject>(values.Length, indexComparer4588);
                foreach (var item in values)
                    result.Add(IndexKey4588(item), item);
                return result;
            }
        
            global::System.Guid IndexKey4588(global::BEngine.BObject item) => item.Id;
        }
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
        Unpack(root);

        void Unpack(GameObject gameObject)
        {
            // Asset equality does not identify an instance boundary: a nested instance
            // may use the same asset as its parent. Preserve that entire branch.
            if (!completely && !ReferenceEquals(gameObject, root) && gameObject.PrefabInstance is not null) return;
            if (completely || ReferenceEquals(gameObject, root)) gameObject.PrefabInstance = null;
            Clear(gameObject);
            for (var index = 0; index < gameObject.components.Count; index++) Clear(gameObject.components[index]);
            for (var index = 0; index < gameObject.transform.childCount; index++) Unpack(gameObject.transform.children[index].gameObject);

            void Clear(BObject item)
            {
                if (completely || item.PrefabAssetId == asset)
                { item.PrefabAssetId = null; item.PrefabSourceId = null; }
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
        var complete = BuildIndex9075(PrefabResolver.Ids([source]), null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid> BuildIndex9075(global::System.Collections.Generic.IEnumerable<global::System.Guid> indexSource9075, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer9075)
        {
            ArgumentNullException.ThrowIfNull(indexSource9075, "source");
            var knownCount = indexSource9075.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(indexComparer9075);
            global::System.ReadOnlySpan<global::System.Guid> values;
            if (knownCount && indexSource9075 is global::System.Guid[] array)
                values = array;
            else if (knownCount && indexSource9075 is global::System.Collections.Generic.List<global::System.Guid> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(capacity, indexComparer9075);
                foreach (var item in indexSource9075)
                    result.Add(IndexKey9075(item), IndexValue9075(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(values.Length, indexComparer9075);
                foreach (var item in values)
                    result.Add(IndexKey9075(item), IndexValue9075(item));
                return result;
            }
        
            global::System.Guid IndexKey9075(global::System.Guid id) => id;
            global::System.Guid IndexValue9075(global::System.Guid id) => ids.GetValueOrDefault(id, id);
        }
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
