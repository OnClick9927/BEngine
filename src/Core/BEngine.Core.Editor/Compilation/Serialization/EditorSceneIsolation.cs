using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>Creates isolated, caller-owned mirrors of explicitly supplied scene graphs.
/// No window, current selection, Undo history, or Play Mode policy is changed.</summary>
public static class EditorSceneIsolation
{
    /// <summary>Clones scenes and their referenced objects, preserving shared references,
    /// serialized identities, in-memory values and hierarchy order. Serialization callbacks
    /// are deferred. The caller owns all returned values and must release them on success.</summary>
    /// <remarks>Additional roots include assets only; an unopened scene selected in a UI
    /// does not join the mirrored scene set. Failures release all partially created mirrors.</remarks>
    public static Dictionary<BObject, BObject> CloneScenes(IEnumerable<Scene> scenes,
        IServiceProvider services, IEnumerable<BObject?>? additionalRoots = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(services);
        var sources = scenes.Distinct<Scene>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var scene in sources)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (!scene.isCreated) throw new ObjectDisposedException(nameof(scenes));
        }
        var mirrors = new Dictionary<Scene, Scene>(ReferenceEqualityComparer.Instance);
        using var before = SerializationCallbackUtility.SuppressBeforeSerialize();
        using var after = SerializationCallbackUtility.SuppressAfterDeserialize();
        try
        {
            foreach (var scene in sources) mirrors.Add(scene, CloneScene(scene, services));
            return CopyInMemoryState(mirrors, additionalRoots ?? []);
        }
        catch
        {
            foreach (var scene in mirrors.Values)
                if (scene.isCreated) EditorCallbacks.Invoke(scene, "Release failed scene mirror", scene.Dispose);
            throw;
        }
    }

    /// <summary>Dispatches one deserialization callback per distinct live object, using
    /// its owning scene or fallbackScene for assets. Does not invoke editor validation.</summary>
    public static void InvokeAfterDeserialize(IEnumerable<BObject> objects, Scene fallbackScene)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(fallbackScene);
        foreach (var group in objects.Distinct<BObject>(ReferenceEqualityComparer.Instance)
                     .Where(value => !value.isDestroyed).GroupBy(GetOwningScene))
        {
            using var context = SceneRuntime.EnterSceneContext(group.Key ?? fallbackScene);
            foreach (var value in group)
                SerializationCallbackUtility.AfterDeserialize(value, validateInEditor: false);
        }
    }

    /// <summary>Destroys caller-owned non-scene mirrors once each. Scenes and their
    /// hierarchy are disposed separately by the owning scene session.</summary>
    public static void ReleaseAssets(IEnumerable<BObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        foreach (var value in objects.Distinct<BObject>(ReferenceEqualityComparer.Instance))
            if (value is not (Scene or GameObject or Component) && !value.isDestroyed)
                EditorCallbacks.Invoke(value, "Release isolated asset", () => BObject.Destroy(value));
    }

    private static Scene CloneScene(Scene source, IServiceProvider services)
    {
        if (!source.isCreated)
            throw new ObjectDisposedException(nameof(source), "An unloaded editor Scene cannot enter Play Mode.");
        var document = SceneAssetSerialization.Capture(source, allowTransientObjects: true);
        // Restore only the skeleton here. During domain reload a serialized instance
        // reference still points at the retiring assembly's object. Members and private
        // payloads are rebound after every destination scene/component has been created.
        foreach (var item in document.GameObjects)
        {
            item.Transform.Fields.Clear();
            item.Transform.Graph = null;
            foreach (var component in item.Components)
            {
                component.Fields.Clear();
                component.Graph = null;
            }
        }
        var clone = SceneAssetSerialization.Restore(document, services, resolvePrefabs: false);
        clone.path = source.path;
        clone.isLoaded = source.isLoaded;
        clone.MarkRuntimeOnly();
        return clone;
    }

    private static Dictionary<BObject, BObject> CopyInMemoryState(
        IReadOnlyDictionary<Scene, Scene> runtimeByEditScene,
        IEnumerable<BObject?> additionalRoots)
    {
        var objectMap = new Dictionary<BObject, BObject>(ReferenceEqualityComparer.Instance);
        foreach (var (editScene, runtimeScene) in runtimeByEditScene)
        {
            objectMap.Add(editScene, runtimeScene);
            foreach (var editObject in editScene.gameObjects)
            {
                var runtimeObject = runtimeScene.Find(editObject.Id) ??
                                    throw new InvalidDataException(
                                        $"Play Mode clone is missing GameObject {editObject.Id}.");
                objectMap.Add(editObject, runtimeObject);
                foreach (var editComponent in editObject.components)
                {
                    var runtimeComponent = runtimeObject.components.FirstOrDefault(item => item.Id == editComponent.Id)
                        ?? throw new InvalidDataException($"Scene mirror is missing Component {editComponent.Id}.");
                    objectMap.Add(editComponent, runtimeComponent);
                }
            }
        }

        var runtimeInstances = new HashSet<BObject>(objectMap.Values, ReferenceEqualityComparer.Instance);
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        BObject MapObject(BObject value)
        {
            if (objectMap.TryGetValue(value, out var mapped)) return mapped;
            if (value is Scene or GameObject or Component)
                throw new InvalidDataException(
                    $"Play Mode cannot mirror a reference to scene object '{value.name}' outside the open Scenes.");
            var runtimeObject = CreateRuntimeObject(value);
            if (ReferenceEquals(value, runtimeObject) || runtimeObject.isDestroyed ||
                runtimeObject is Scene or GameObject or Component ||
                !runtimeInstances.Add(runtimeObject))
                throw new InvalidDataException("Object clone providers must return fresh, live non-scene instances.");
            objectMap.Add(value, runtimeObject);
            CopyObjectMembers(value, runtimeObject, MapObject, visited);
            return runtimeObject;
        }

        try
        {
            foreach (var (source, destination) in objectMap.ToArray())
            {
                CopyObjectMetadata(source, destination);
                if (source is GameObject editObject && destination is GameObject runtimeObject)
                {
                    runtimeObject.SetActive(editObject.activeSelf);
                    runtimeObject.tag = editObject.tag;
                    runtimeObject.layer = editObject.layer;
                    runtimeObject.isStatic = editObject.isStatic;
                }
                if (source is Component editComponent && destination is Component runtimeComponent)
                {
                    CopyObjectMembers(editComponent, runtimeComponent, MapObject, visited);
                    runtimeComponent.enabled = editComponent.enabled;
                }
            }

            foreach (var root in additionalRoots)
            {
                // Selecting an unopened .scene asset in Project is not a reference
                // from the running scene graph. It must not prevent entering Play.
                if (root is not BAsset || root is Scene || objectMap.ContainsKey(root)) continue;
                MapObject(root);
            }

            foreach (var (editScene, runtimeScene) in runtimeByEditScene)
            {
                var editRoots = editScene.rootGameObjects.ToArray();
                for (var index = 0; index < editRoots.Length; index++)
                {
                    var runtimeRoot = runtimeScene.Find(editRoots[index].Id) ??
                                      throw new InvalidDataException(
                                          $"Play Mode clone is missing root GameObject {editRoots[index].Id}.");
                    runtimeRoot.transform.SetSiblingIndex(index);
                    RestoreChildOrder(editRoots[index].transform, runtimeRoot.transform);
                }
            }
            return objectMap;
        }
        catch
        {
            ReleaseAssets(objectMap.Values);
            throw;
        }
    }

    private static BObject CreateRuntimeObject(BObject source)
    {
        if (source is BEngine.TextAsset textAsset)
            return new BEngine.TextAsset(textAsset.text, textAsset.path);
        if (source is PrefabAsset prefab)
        {
            return PrefabAssetSerialization.CreateAsset(AssetSnapshotClone.Clone(prefab.Data), prefab.assetPath);
        }
        var sourceType = source.GetType();
        var runtimeType = RuntimeTypeCache.ResolveSerializedType(
            sourceType.AssemblyQualifiedName ?? sourceType.FullName ?? string.Empty) ?? sourceType;
        if (!typeof(BObject).IsAssignableFrom(runtimeType)) runtimeType = sourceType;
        if (RuntimeObjectCodecs.Find(runtimeType) is { } codec)
            return codec.CreateCloneInstance();
        if (source is ScriptableObject)
            return ScriptableObject.CreateInstance(runtimeType);
        if (RuntimeTypeCache.GetFactory(runtimeType)?.Invoke() is BObject runtimeObject)
            return runtimeObject;
        throw new NotSupportedException(
            $"Play Mode cannot isolate referenced object type '{source.GetType().FullName}'.");
    }

    private static void CopyObjectMetadata(BObject source, BObject destination)
    {
        destination.IsRuntimeOnly = true;
        destination.name = source.name;
        destination.hideFlags = source.hideFlags;
        destination.PrefabAssetId = source.PrefabAssetId;
        destination.PrefabSourceId = source.PrefabSourceId;
        if (source is BAsset sourceAsset && destination is BAsset destinationAsset)
            destinationAsset.CopyAssetReferenceFrom(sourceAsset);
    }

    private static void CopyObjectMembers(
        BObject source,
        BObject destination,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        CopyObjectMetadata(source, destination);
        if (source is BAsset && destination is BAsset)
            destination.CopyClonePayloadFrom(source, objectMapper);
        var destinationMembers = EditorObjectSnapshot.GetSerializableMembers(destination)
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var member in EditorObjectSnapshot.GetSerializableMembers(source))
        {
            if (!destinationMembers.TryGetValue(member.Name, out var destinationMember)) continue;
            var sourceAccessor = RuntimeTypeCache.GetMemberAccessor(member);
            var destinationAccessor = RuntimeTypeCache.GetMemberAccessor(destinationMember);
            if (destinationAccessor.Setter is null) continue;
            try
            {
                var value = EditorValueCloner.CloneValue(
                    sourceAccessor.Getter(source), destinationAccessor.ValueType, objectMapper, visited);
                destinationAccessor.Setter(destination, value);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
            {
                throw new InvalidDataException(
                    $"Play Mode could not copy member '{member.Name}' from " +
                    $"{EditorValueCloner.DescribeType(source.GetType())} to " +
                    $"{EditorValueCloner.DescribeType(destination.GetType())}. {exception.Message}", exception);
            }
        }
        if (source is not BAsset)
            destination.CopyClonePayloadFrom(source, objectMapper);
        if (destination is BAsset asset) asset.MarkTransient();
    }

    private static void RestoreChildOrder(Transform editParent, Transform runtimeParent)
    {
        var editChildren = editParent.children.ToArray();
        for (var index = 0; index < editChildren.Length; index++)
        {
            var runtimeChild = runtimeParent.children.FirstOrDefault(item => item.gameObject.Id == editChildren[index].gameObject.Id)
                ?? throw new InvalidDataException($"Scene mirror is missing child GameObject {editChildren[index].gameObject.Id}.");
            runtimeChild.SetSiblingIndex(index);
            RestoreChildOrder(editChildren[index], runtimeChild);
        }
    }

    private static Scene? GetOwningScene(BObject value) => value switch
    {
        Scene scene => scene,
        GameObject gameObject => gameObject.scene,
        Component component => component.gameObject.scene,
        _ => null
    };
}
