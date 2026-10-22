using System.Collections;
using BEngine.Serialization;

namespace BEngine.Editor;

public enum PrefabOverrideKind { Property, AddedComponent, RemovedComponent, AddedGameObject, RemovedGameObject }

public sealed record PrefabOverride(GameObject InstanceRoot, BObject Target, PrefabOverrideKind Kind,
    string PropertyPath, Guid SourceId);

public static partial class PrefabUtility
{
    public static PrefabAsset SaveAsPrefabVariant(GameObject instanceRoot, string assetPath, bool connect = true)
    {
        EnsurePrefabEditing(instanceRoot);
        var sourcePath = GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("A variant requires a prefab source.");
        if (ResolveAssetPath(sourcePath).FullPath.Equals(ResolveAssetPath(assetPath).FullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save the variant to a different asset path.");
        var source = LoadPrefabAsset(sourcePath);
        return SaveAsset(instanceRoot, assetPath, connect, out _, source)!;
    }

    public static IReadOnlyList<PrefabOverride> GetOverrides(GameObject instanceRoot, bool includeDefaultOverrides = false)
    {
        using var context = new OverrideContext(instanceRoot);
        var result = new List<PrefabOverride>();
        var liveObjects = PrefabAssetOperations.Traverse(context.Root).ToArray();
        foreach (var gameObject in liveObjects)
        {
            if (!context.TrySource(gameObject, out var sourceObject))
            {
                if (gameObject.transform.parent is null || context.TrySource(gameObject.transform.parent.gameObject, out _))
                    result.Add(new(context.Root, gameObject, PrefabOverrideKind.AddedGameObject, "", Guid.Empty));
                continue;
            }
            Compare(gameObject, sourceObject);
            foreach (var component in gameObject.components)
            {
                if (!context.TrySource(component, out var sourceComponent))
                    result.Add(new(context.Root, component, PrefabOverrideKind.AddedComponent, "", Guid.Empty));
                else Compare(component, sourceComponent);
            }
        }
        foreach (var sourceObject in PrefabAssetOperations.Traverse(context.Contents))
        {
            if (!context.TryLive(sourceObject, out var liveObject))
            {
                if (sourceObject.transform.parent is { } parent && context.TryLive(parent.gameObject, out _))
                    result.Add(new(context.Root, context.Root, PrefabOverrideKind.RemovedGameObject, sourceObject.name, sourceObject.Id));
                continue;
            }
            foreach (var component in sourceObject.components)
                if (component is not Transform && !context.TryLive(component, out _))
                    result.Add(new(context.Root, liveObject, PrefabOverrideKind.RemovedComponent, component.GetType().Name, component.Id));
        }
        return result;

        void Compare(BObject live, BObject source)
        {
            using var liveSerialized = new SerializedObject(live);
            using var sourceSerialized = new SerializedObject(source);
            foreach (var path in PropertyPaths(live, liveSerialized))
            {
                if (!includeDefaultOverrides && ReferenceEquals(live, context.Root.transform) && path is "localPosition" or "localRotation") continue;
                var property = liveSerialized.FindProperty(path);
                var inherited = sourceSerialized.FindProperty(path);
                if (property is not null && inherited is not null) CompareProperty(property, inherited, 0);
            }
            void CompareProperty(SerializedProperty property, SerializedProperty inherited, int depth)
            {
                if (!property.editable || ValuesEqual(property.boxedValue, inherited.boxedValue, context)) return;
                var children = depth < 16 && !property.isArray
                    ? liveSerialized.GetTraversalChildren(property, visibleOnly: false) : [];
                if (children.Count > 0 && children.All(child => sourceSerialized.FindProperty(child.propertyPath) is not null))
                {
                    foreach (var child in children) CompareProperty(child, sourceSerialized.FindProperty(child.propertyPath)!, depth + 1);
                }
                else result.Add(new(context.Root, live, PrefabOverrideKind.Property, property.propertyPath, source.Id));
            }
        }
    }

    public static void ApplyPropertyOverride(SerializedProperty property, string assetPath,
        InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(property);
        EditorAssetWritePolicy.EnsureCanWrite("Applying a prefab property");
        if (property.serializedObject.targetObjects.Length != 1) throw new ArgumentException("Apply one prefab target at a time.");
        using var context = new OverrideContext(property.serializedObject.targetObject, assetPath);
        EditInstance(context.Root, "Apply Prefab Property", action, () =>
        {
            var source = context.Source(property.serializedObject.targetObject);
            CopyProperty(property.serializedObject.targetObject, source, property.propertyPath, context.ToSource);
            context.Save(action);
        });
    }

    public static void RevertPropertyOverride(SerializedProperty property, InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(property);
        EditorAssetWritePolicy.EnsureCanWrite("Reverting a prefab property");
        using var context = new OverrideContext(property.serializedObject.targetObject);
        RevertProperty(context, property.serializedObject.targetObject, property.propertyPath, action);
    }

    public static void ApplyObjectOverride(BObject target, string assetPath, InteractionMode action = InteractionMode.UserAction)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Applying a prefab object");
        using var context = new OverrideContext(target, assetPath);
        EditInstance(context.Root, "Apply Prefab Object", action, () =>
        {
            CopyObject(target, context.Source(target), context.ToSource);
            context.Save(action);
        });
    }

    public static void RevertObjectOverride(BObject target, InteractionMode action = InteractionMode.UserAction)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Reverting a prefab object");
        using var context = new OverrideContext(target);
        EditInstance(context.Root, "Revert Prefab Object", action, () =>
        {
            var before = LiveSnapshot(context.Root);
            CopyObject(context.Source(target), target, context.ToLive);
            AcceptChanges(context.Root, before, LiveSnapshot(context.Root));
        });
    }

    public static void ApplyOverride(PrefabOverride item, InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsurePrefabEditing(item.InstanceRoot);
        using var context = new OverrideContext(item.Target, GetPrefabAssetPathOfNearestInstanceRoot(item.InstanceRoot));
        EditInstance(context.Root, "Apply Prefab Override", action, () =>
        {
            switch (item.Kind)
            {
                case PrefabOverrideKind.Property:
                    CopyProperty(item.Target, context.Source(item.Target), item.PropertyPath, context.ToSource); break;
                case PrefabOverrideKind.AddedComponent:
                    var component = (Component)item.Target;
                    var owner = (GameObject)context.Source(component.gameObject);
                    var added = owner.AddComponent(component.GetType()); added.Id = component.Id;
                    context.AddMapping(added, component);
                    component.PrefabAssetId = context.Asset.assetId; component.PrefabSourceId = added.Id;
                    CopyObject(component, added, context.ToSource); break;
                case PrefabOverrideKind.RemovedComponent:
                    var removed = (Component)context.SourceObjects[item.SourceId];
                    if (!removed.gameObject.RemoveComponent(removed)) throw new InvalidOperationException("Another component requires this component.");
                    break;
                case PrefabOverrideKind.AddedGameObject:
                    CopySubtree(context, (GameObject)item.Target, toSource: true); break;
                case PrefabOverrideKind.RemovedGameObject:
                    BObject.DestroyImmediate((GameObject)context.SourceObjects[item.SourceId]); break;
                default: throw new ArgumentOutOfRangeException(nameof(item));
            }
            context.Save(action);
        });
    }

    public static void RevertOverride(PrefabOverride item, InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsurePrefabEditing(item.InstanceRoot);
        using var context = new OverrideContext(item.Target, GetPrefabAssetPathOfNearestInstanceRoot(item.InstanceRoot));
        if (item.Kind == PrefabOverrideKind.Property) { RevertProperty(context, item.Target, item.PropertyPath, action); return; }
        EditInstance(context.Root, "Revert Prefab Override", action, () =>
        {
            var before = LiveSnapshot(context.Root);
            switch (item.Kind)
            {
                case PrefabOverrideKind.AddedComponent:
                    var added = (Component)item.Target;
                    if (!added.gameObject.RemoveComponent(added)) throw new InvalidOperationException("Another component requires this component.");
                    break;
                case PrefabOverrideKind.RemovedComponent:
                    var source = (Component)context.SourceObjects[item.SourceId];
                    var restored = ((GameObject)item.Target).AddComponent(source.GetType());
                    restored.Id = context.SourceIds.GetValueOrDefault(source.Id, Guid.NewGuid());
                    restored.PrefabAssetId = source.PrefabAssetId ?? context.Asset.assetId;
                    restored.PrefabSourceId = source.PrefabSourceId ?? source.Id;
                    context.AddMapping(source, restored);
                    CopyObject(source, restored, context.ToLive); break;
                case PrefabOverrideKind.AddedGameObject:
                    BObject.DestroyImmediate((GameObject)item.Target); break;
                case PrefabOverrideKind.RemovedGameObject:
                    CopySubtree(context, (GameObject)context.SourceObjects[item.SourceId], toSource: false); break;
                default: throw new ArgumentOutOfRangeException(nameof(item));
            }
            AcceptChanges(context.Root, before, LiveSnapshot(context.Root));
        });
    }

    private static void RevertProperty(OverrideContext context, BObject target, string path, InteractionMode action) =>
        EditInstance(context.Root, "Revert Prefab Property", action, () =>
        {
            var before = LiveSnapshot(context.Root);
            CopyProperty(context.Source(target), target, path, context.ToLive);
            AcceptChanges(context.Root, before, LiveSnapshot(context.Root));
        });

    // Register every object before copying fields so sibling, child and cyclic references
    // resolve within the new subtree. Existing objects and their overrides are retained.
    private static void CopySubtree(OverrideContext context, GameObject root, bool toSource)
    {
        var originals = PrefabAssetOperations.Traverse(root).ToArray();
        Func<BObject, BObject> map = toSource ? context.ToSource : context.ToLive;
        if (root.transform.parent is null) throw new InvalidOperationException("The prefab root cannot be a hierarchy override.");
        var parent = (Transform)map(root.transform.parent);
        foreach (var original in originals)
        {
            var copy = new GameObject(original.name);
            (toSource ? context.Contents.scene : context.Root.scene)?.Add(copy);
            copy.transform.SetParent(parent, false);
            if (copy.transform.GetType() != original.transform.GetType()) copy.AddComponent(original.transform.GetType());
            Link(original, copy);
            Link(original.transform, copy.transform);
            var assigned = new HashSet<Component> { copy.transform };
            foreach (var component in original.components.Where(value => value is not Transform))
            {
                var added = copy.components.FirstOrDefault(value => value.GetType() == component.GetType() && !assigned.Contains(value))
                    ?? copy.AddComponent(component.GetType());
                assigned.Add(added); Link(component, added);
            }
        }
        foreach (var original in originals)
        {
            var copy = (GameObject)map(original);
            copy.transform.SetParent((Transform)map(original.transform.parent!), false);
            copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex());
            CopyObject(original, copy, map);
            foreach (var component in original.components) CopyObject(component, map(component), map);
            if (original.PrefabInstance is { } nested)
            {
                var ids = originals.SelectMany(value => value.components.Cast<BObject>().Prepend(value))
                    .ToDictionary(value => value.Id, value => map(value).Id);
                copy.PrefabInstance = PrefabResolver.RemapReference(nested, ids);
            }
        }
        void Link(BObject original, BObject copy)
        {
            if (toSource)
            {
                copy.Id = original.Id;
                copy.PrefabAssetId = original.PrefabAssetId; copy.PrefabSourceId = original.PrefabSourceId;
                context.AddMapping(copy, original);
                original.PrefabAssetId ??= context.Asset.assetId; original.PrefabSourceId ??= copy.Id;
            }
            else
            {
                copy.Id = context.SourceIds.GetValueOrDefault(original.Id, Guid.NewGuid());
                copy.PrefabAssetId = original.PrefabAssetId ?? context.Asset.assetId;
                copy.PrefabSourceId = original.PrefabSourceId ?? original.Id;
                context.AddMapping(original, copy);
            }
        }
    }

    private static void CopyProperty(BObject source, BObject target, string path, Func<BObject, BObject> mapper)
    {
        using var sourceObject = new SerializedObject(source); using var targetObject = new SerializedObject(target);
        var from = sourceObject.FindProperty(path) ?? throw new ArgumentException($"Unknown source property {path}.");
        var to = targetObject.FindProperty(path) ?? throw new ArgumentException($"Unknown target property {path}.");
        if (!to.editable) throw new InvalidOperationException($"Property {path} is read-only.");
        PropertyPath.Resolve(target, path).SetValue(EditorValueCloner.CloneValue(from.boxedValue, mapper, new(ReferenceEqualityComparer.Instance)));
        EditorUtility.SetDirty(target);
    }

    private static void CopyObject(BObject source, BObject target, Func<BObject, BObject> mapper)
    {
        using var sourceObject = new SerializedObject(source); using var targetObject = new SerializedObject(target);
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var path in PropertyPaths(source, sourceObject))
            if (sourceObject.FindProperty(path) is { } from && targetObject.FindProperty(path) is { editable: true } to)
                PropertyPath.Resolve(target, path).SetValue(EditorValueCloner.CloneValue(from.boxedValue, mapper, visited));
        EditorUtility.SetDirty(target);
    }

    private static IEnumerable<string> PropertyPaths(BObject target, SerializedObject serialized) => target switch
    {
        GameObject => ["name", "activeSelf", "tag", "layer", "isStatic"],
        Transform => serialized.GetPropertyPaths().Concat(["localPosition", "localRotation", "localScale"]).Distinct(),
        Component => serialized.GetPropertyPaths().Append("enabled").Distinct(),
        _ => serialized.GetPropertyPaths()
    };

    private static bool ValuesEqual(object? current, object? source, OverrideContext context,
        HashSet<(object, object)>? visited = null)
    {
        if (ReferenceEquals(current, source)) return true;
        if (current is null || source is null || current.GetType() != source.GetType()) return false;
        if (current is BObject engineObject && source is BObject sourceObject)
            return engineObject is GameObject or Component ? context.TrySource(engineObject, out var mapped) && mapped.Id == sourceObject.Id
                : engineObject.Id == sourceObject.Id;
        if (current is string || current.GetType().IsValueType) return current.Equals(source);
        visited ??= [];
        if (!visited.Add((current, source))) return true;
        if (current is IList list && source is IList sourceList)
            return list.Count == sourceList.Count && Enumerable.Range(0, list.Count).All(index => ValuesEqual(list[index], sourceList[index], context, visited));
        if (current is IDictionary dictionary && source is IDictionary sourceDictionary)
            return dictionary.Count == sourceDictionary.Count && dictionary.Keys.Cast<object>().All(key =>
                sourceDictionary.Contains(key) && ValuesEqual(dictionary[key], sourceDictionary[key], context, visited));
        return RuntimeTypeCache.GetInstanceMembers(current.GetType()).Where(RuntimeTypeCache.IsSerializableMember)
            .All(member => ValuesEqual(RuntimeTypeCache.GetMemberAccessor(member).Getter(current),
                RuntimeTypeCache.GetMemberAccessor(member).Getter(source), context, visited));
    }

    private static void RestoreHierarchy(OverrideContext context)
    {
        var oldObjects = PrefabAssetOperations.Traverse(context.Root).ToArray();
        var desired = new HashSet<GameObject>();
        var desiredComponents = new HashSet<Component>();
        foreach (var source in PrefabAssetOperations.Traverse(context.Contents))
        {
            GameObject live;
            if (context.TryLive(source, out var existing)) live = (GameObject)existing;
            else
            {
                live = new GameObject(source.name) { Id = context.SourceIds[source.Id] };
                context.Root.scene?.Add(live);
                // Keep partially constructed additions inside the rollback scope.
                live.transform.SetParent(context.Root.transform, false);
                context.AddMapping(source, live);
            }
            desired.Add(live);
            if (live.transform.GetType() != source.transform.GetType()) live.AddComponent(source.transform.GetType());
            live.transform.Id = context.SourceIds[source.transform.Id];
            context.AddMapping(source.transform, live.transform);
            desiredComponents.Add(live.transform);
            Link(source, live); Link(source.transform, live.transform);
            foreach (var sourceComponent in source.components.Where(item => item is not Transform))
            {
                Component component;
                if (context.TryLive(sourceComponent, out var current) && current is Component candidate && candidate.GetType() == sourceComponent.GetType())
                    component = candidate;
                else
                {
                    component = live.components.FirstOrDefault(candidate => candidate is not Transform &&
                        candidate.GetType() == sourceComponent.GetType() && !desiredComponents.Contains(candidate) &&
                        !context.SourceIds.Values.Contains(candidate.Id)) ?? live.AddComponent(sourceComponent.GetType());
                    component.Id = context.SourceIds[sourceComponent.Id];
                    context.AddMapping(sourceComponent, component);
                }
                desiredComponents.Add(component); Link(sourceComponent, component);
            }
        }
        foreach (var source in PrefabAssetOperations.Traverse(context.Contents))
        {
            var live = (GameObject)context.ToLive(source);
            if (!ReferenceEquals(live, context.Root))
                live.transform.SetParent((Transform)context.ToLive(source.transform.parent!), false);
            var removals = live.components.Where(item => !desiredComponents.Contains(item)).ToList();
            while (removals.Count > 0)
            {
                var count = removals.RemoveAll(item => live.RemoveComponent(item));
                if (count == 0) throw new InvalidOperationException("Cannot restore the prefab's required component dependencies.");
            }
        }
        foreach (var obsolete in oldObjects.Where(item => !desired.Contains(item) &&
                     (item.transform.parent is null || desired.Contains(item.transform.parent.gameObject))).ToArray())
            BObject.DestroyImmediate(obsolete);
        foreach (var source in PrefabAssetOperations.Traverse(context.Contents))
        {
            var live = (GameObject)context.ToLive(source);
            CopyObject(source, live, context.ToLive);
            foreach (var component in source.components) CopyObject(component, context.ToLive(component), context.ToLive);
            if (!ReferenceEquals(live, context.Root)) live.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
            live.PrefabInstance = source.PrefabInstance is { } nested && !ReferenceEquals(source, context.Contents)
                ? PrefabResolver.RemapReference(nested, context.SourceIds) : null;
        }
        context.Root.PrefabInstance = context.PreserveInstanceMetadata
            ? PrefabMerge.Clone(context.Contents.PrefabInstance!) : new PrefabReferenceData
        {
            Asset = PrefabResolver.Reference(context.Asset), Root = context.Root.Id,
            ObjectIds = new(context.SourceIds), Baseline = context.MapSource()
        };
        void Link(BObject source, BObject live)
        {
            live.PrefabAssetId = context.PreserveInstanceMetadata ? source.PrefabAssetId : source.PrefabAssetId ?? context.Asset.assetId;
            live.PrefabSourceId = context.PreserveInstanceMetadata ? source.PrefabSourceId : source.PrefabSourceId ?? source.Id;
        }
    }

    private static List<GameObjectData> LiveSnapshot(GameObject root) =>
        PrefabAssetOperations.Traverse(root).Select(item => SceneAssetSerialization.FromGameObject(item)).ToList();

    private static void AcceptChanges(GameObject root, List<GameObjectData> before, List<GameObjectData> after)
    {
        for (var current = root; current is not null; current = current.transform.parent?.gameObject)
            if (current.PrefabInstance is { } reference)
                reference.Baseline = PrefabMerge.Merge(before, after, reference.Baseline);
    }

    private static void EditInstance(GameObject root, string name, InteractionMode action, Action edit)
    {
        EnsurePrefabEditing(root);
        var before = FullHierarchyState.Capture(root);
        try { edit(); }
        catch { before.CaptureInverse(); before.Restore(); throw; }
        var after = before.CaptureInverse();
        if (action == InteractionMode.UserAction) Undo.RegisterOperation(name, before.Restore, after.Restore, root);
        EditorUtility.SetDirty(root); RaiseInstanceUpdated(root);
    }

    private static void EnsurePrefabEditing(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!EditorAssetWritePolicy.CanWrite || root.IsRuntimeOnly || root.scene?.IsRuntimeOnly == true)
            throw new InvalidOperationException("Prefab authoring is unavailable in Play Mode.");
    }

    private sealed class OverrideContext : IDisposable
    {
        internal GameObject Root { get; }
        internal PrefabAsset Asset { get; }
        internal string Path { get; }
        internal GameObject Contents { get; }
        internal Dictionary<Guid, BObject> SourceObjects { get; }
        internal Dictionary<Guid, Guid> SourceIds { get; }
        private readonly Dictionary<Guid, BObject> _live;
        private readonly Dictionary<Guid, Guid> _sourceIds;
        private readonly List<GameObjectData> _before;
        internal bool PreserveInstanceMetadata { get; }
        private readonly Dictionary<Guid, BObject> _external = [];

        internal OverrideContext(GameObject root, SceneAssetData desired)
        {
            Root = root; PreserveInstanceMetadata = true;
            Asset = PrefabResolver.Load(root.PrefabInstance!.Asset); Path = Asset.assetPath;
            if (root.scene is { } scene)
                foreach (var value in scene.gameObjects.SelectMany(item => item.components.Cast<BObject>().Prepend(item)))
                    _external[value.Id] = value;
            desired.GameObjects.Single(item => item.Id == root.Id).Parent = null;
            using var temporary = SceneAssetSerialization.Restore(desired, resolvePrefabs: false,
                externalReferenceResolver: id => _external.GetValueOrDefault(id));
            Contents = temporary.Find(root.Id)!;
            temporary.ReleaseAll();
            SourceObjects = Objects(Contents).ToDictionary(item => item.Id);
            _live = Objects(root).ToDictionary(item => item.Id);
            SourceIds = SourceObjects.Keys.ToDictionary(id => id, id => id);
            _sourceIds = new(SourceIds); _before = [];
        }

        internal OverrideContext(BObject target, string? path = null)
        {
            Root = GetNearestPrefabInstanceRoot(target) ?? throw new InvalidOperationException("Object is not inside a prefab instance.");
            if (path is not null)
            {
                var expected = ResolveAssetPath(path).FullPath;
                for (var current = ToGameObject(target); current is not null; current = current.transform.parent?.gameObject)
                    if (IsAnyPrefabInstanceRoot(current) && ResolveAssetPath(GetPrefabAssetPathOfNearestInstanceRoot(current)).FullPath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    { Root = current; break; }
                if (!ResolveAssetPath(GetPrefabAssetPathOfNearestInstanceRoot(Root)).FullPath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The requested prefab is not an ancestor source of this instance.");
            }
            Path = GetPrefabAssetPathOfNearestInstanceRoot(Root); Asset = LoadPrefabAsset(Path);
            Contents = PrefabAssetOperations.LoadContents(Asset);
            SourceObjects = Objects(Contents).ToDictionary(item => item.Id);
            _live = Objects(Root).ToDictionary(item => item.Id);
            _sourceIds = PrefabAssetSerialization.CaptureIdMap(Root);
            SourceIds = _sourceIds.ToDictionary(pair => pair.Value, pair => pair.Key);
            if (Root.PrefabInstance is { } reference)
                foreach (var pair in reference.ObjectIds) SourceIds.TryAdd(pair.Key, pair.Value);
            _before = MapSource();
        }
        private static IEnumerable<BObject> Objects(GameObject root) => PrefabAssetOperations.Traverse(root)
            .SelectMany(item => item.components.Cast<BObject>().Prepend(item));
        internal bool TrySource(BObject live, out BObject source) => SourceObjects.TryGetValue(_sourceIds.GetValueOrDefault(live.Id, live.Id), out source!);
        internal bool TryLive(BObject source, out BObject live) => _live.TryGetValue(SourceIds.GetValueOrDefault(source.Id, source.Id), out live!);
        internal BObject Source(BObject live) => TrySource(live, out var source) ? source : throw new InvalidOperationException("Object has not been added to the prefab source.");
        internal BObject ToSource(BObject value) => value is GameObject or Component ? Source(value) : value;
        internal BObject ToLive(BObject value) => value is GameObject or Component ? TryLive(value, out var live) ? live :
            PreserveInstanceMetadata && _external.TryGetValue(value.Id, out var external) ? external :
            throw new InvalidOperationException("The referenced source object was removed from the instance; restore it first.") : value;
        internal void AddMapping(BObject source, BObject live)
        {
            SourceObjects[source.Id] = source; _live[live.Id] = live;
            SourceIds[source.Id] = live.Id; _sourceIds[live.Id] = source.Id;
            if (Root.PrefabInstance is { } reference) reference.ObjectIds[source.Id] = live.Id;
        }
        internal List<GameObjectData> MapSource()
        {
            var source = PrefabAssetSerialization.Capture(Contents, Asset.assetId);
            var mapped = PrefabResolver.Map(source, new PrefabReferenceData
            { Root = Root.Id, ObjectIds = SourceIds }, connected: true).GameObjects;
            mapped.Single(item => item.Id == Root.Id).Parent = Root.transform.parent?.gameObject.Id;
            return mapped;
        }
        internal void Save(InteractionMode action)
        {
            if (action == InteractionMode.UserAction) RegisterPrefabFileUndo(Path, "Apply Prefab Override");
            SaveAsset(Contents, Path, connect: false, out _);
            AcceptChanges(Root, _before, MapSource());
        }
        public void Dispose() => UnloadPrefabContents(Contents);
    }

    private static void RegisterPrefabFileUndo(string path, string name)
    {
        var resolved = ResolveAssetPath(path);
        Undo.RegisterFileUndo(resolved.FullPath, resolved.ProjectPath, name);
    }
}
