using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>Owns detached prefab contents and source/instance identity mapping for one coordinated edit.
/// Source resolution uses the registered backend; file commits, history, dirty markers and presentation stay with the caller.
/// Callers provide serialized-value copying and wrap mutations in their editor transaction.</summary>
public class EditorPrefabEditSession : IDisposable
{
    public GameObject Root { get; }
    public PrefabAsset Asset { get; }
    public GameObject Contents { get; }
    private Dictionary<Guid, BObject> SourceObjects { get; }
    private Dictionary<Guid, Guid> SourceIds { get; }
    private readonly Dictionary<Guid, BObject> _live;
    private readonly Dictionary<Guid, Guid> _sourceIds;
    private readonly List<GameObjectData> _before;
    private bool PreserveInstanceMetadata { get; }
    private readonly Dictionary<Guid, BObject> _external = [];
    private bool _disposed;
    private Dictionary<Guid, BObject> OpenSourceObjects { get { EnsureOpen(); return SourceObjects; } }
    private Dictionary<Guid, BObject> OpenLiveObjects { get { EnsureOpen(); return _live; } }

    protected EditorPrefabEditSession(GameObject root, PrefabAsset asset)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(root.isDestroyed, root);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        Root = root; Asset = asset;
        Contents = PrefabAssetOperations.LoadContents(Asset);
        try
        {
            SourceObjects = Objects(Contents).ToDictionary(item => item.Id);
            _live = Objects(Root).ToDictionary(item => item.Id);
            _sourceIds = PrefabAssetSerialization.CaptureIdMap(Root);
            SourceIds = _sourceIds.ToDictionary(pair => pair.Value, pair => pair.Key);
            if (Root.PrefabInstance is { } reference)
                foreach (var pair in reference.ObjectIds) SourceIds.TryAdd(pair.Key, pair.Value);
            _before = MapSource();
        }
        catch { BObject.DestroyImmediate(Contents); throw; }
    }
    internal EditorPrefabEditSession(GameObject root, SceneAssetData desired)
    {
        Root = root; PreserveInstanceMetadata = true;
        Asset = PrefabResolver.Load(root.PrefabInstance!.Asset);
        if (root.scene is { } scene)
            foreach (var value in scene.gameObjects.SelectMany(item => item.components.Cast<BObject>().Prepend(item)))
                _external[value.Id] = value;
        Gather34871(desired.GameObjects).Parent = null;
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Serialization.GameObjectData Gather34871(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData> items34871)
        {
            ArgumentNullException.ThrowIfNull(items34871, "source");
            bool Evaluate34871(global::BEngine.Serialization.GameObjectData item) => item.Id == root.Id;
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> values;
            if (items34871.GetType() == typeof(global::BEngine.Serialization.GameObjectData[]))
                values = (global::BEngine.Serialization.GameObjectData[])items34871;
            else if (items34871.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>)items34871);
            else
                return global::System.Linq.Enumerable.Single(items34871, Evaluate34871);
            var found = false;
            global::BEngine.Serialization.GameObjectData result = default!;
            foreach (var item in values)
            {
                if (!Evaluate34871(item))
                    continue;
                if (found)
                    return global::System.Linq.Enumerable.Single(new global::BEngine.Serialization.GameObjectData[2], static _ => true);
                result = item;
                found = true;
            }

            if (found)
                return result;
            return global::System.Linq.Enumerable.Single(global::System.Array.Empty<global::BEngine.Serialization.GameObjectData>(), static _ => true);
        }
        using var temporary = SceneAssetSerialization.Restore(desired, resolvePrefabs: false,
            externalReferenceResolver: id => _external.GetValueOrDefault(id));
        Contents = temporary.Find(root.Id)!;
        temporary.ReleaseAll();
        SourceObjects = Objects(Contents).ToDictionary(item => item.Id);
        _live = Objects(root).ToDictionary(item => item.Id);
        SourceIds = SourceObjects.Keys.ToDictionary(id => id, id => id);
        _sourceIds = new(SourceIds); _before = [];
    }

    private static IEnumerable<BObject> Objects(GameObject root) => PrefabAssetOperations.Traverse(root)
        .SelectMany(item => item.components.Cast<BObject>().Prepend(item));
    public bool TrySource(BObject live, out BObject source) => OpenSourceObjects.TryGetValue(_sourceIds.GetValueOrDefault(live.Id, live.Id), out source!);
    public bool TryLive(BObject source, out BObject live) => OpenLiveObjects.TryGetValue(SourceIds.GetValueOrDefault(source.Id, source.Id), out live!);
    public BObject Source(BObject live) => TrySource(live, out var source) ? source : throw new InvalidOperationException("Object has not been added to the prefab source.");
    public BObject ToSource(BObject value) => value is GameObject or Component ? Source(value) : value;
    public BObject ToLive(BObject value) => value is GameObject or Component ? TryLive(value, out var live) ? live :
        PreserveInstanceMetadata && _external.TryGetValue(value.Id, out var external) ? external :
        throw new InvalidOperationException("The referenced source object was removed from the instance; restore it first.") : value;
    private void AddMapping(BObject source, BObject live)
    {
        SourceObjects[source.Id] = source; _live[live.Id] = live;
        SourceIds[source.Id] = live.Id; _sourceIds[live.Id] = source.Id;
        if (Root.PrefabInstance is { } reference) reference.ObjectIds[source.Id] = live.Id;
    }
    private List<GameObjectData> MapSource()
    {
        var source = PrefabAssetSerialization.Capture(Contents, Asset.assetId);
        var mapped = PrefabResolver.Map(source, new PrefabReferenceData
        { Root = Root.Id, ObjectIds = SourceIds }, connected: true).GameObjects;
        Gather38955(mapped).Parent = Root.transform.parent?.gameObject.Id;
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Serialization.GameObjectData Gather38955(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData> items38955)
        {
            ArgumentNullException.ThrowIfNull(items38955, "source");
            bool Evaluate38955(global::BEngine.Serialization.GameObjectData item) => item.Id == Root.Id;
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> values;
            if (items38955.GetType() == typeof(global::BEngine.Serialization.GameObjectData[]))
                values = (global::BEngine.Serialization.GameObjectData[])items38955;
            else if (items38955.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>)items38955);
            else
                return global::System.Linq.Enumerable.Single(items38955, Evaluate38955);
            var found = false;
            global::BEngine.Serialization.GameObjectData result = default!;
            foreach (var item in values)
            {
                if (!Evaluate38955(item))
                    continue;
                if (found)
                    return global::System.Linq.Enumerable.Single(new global::BEngine.Serialization.GameObjectData[2], static _ => true);
                result = item;
                found = true;
            }

            if (found)
                return result;
            return global::System.Linq.Enumerable.Single(global::System.Array.Empty<global::BEngine.Serialization.GameObjectData>(), static _ => true);
        }
        return mapped;
    }
    private static void CopySubtree(EditorPrefabEditSession context, GameObject root, bool toSource, Action<BObject, BObject, Func<BObject, BObject>> copyValues)
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
            {
                var __filteredSource12717 = original.components;
                ArgumentNullException.ThrowIfNull(__filteredSource12717, "source");
                foreach (var component in __filteredSource12717)
                {
                    if (!(component is not Transform))
                        continue;
                    var added = Matches14592() ?? copy.AddComponent(component.GetType());
                    [global::BEngine.SkipLoopGuard]
                    global::BEngine.Component? Matches14592()
                    {
                        var matchSource14592 = copy.components;
                        ArgumentNullException.ThrowIfNull(matchSource14592, "source");
                        global::System.ReadOnlySpan<global::BEngine.Component> matchValues14592;
                        if (matchSource14592.GetType() == typeof(global::BEngine.Component[]))
                        {
                            matchValues14592 = (global::BEngine.Component[])(object)matchSource14592;
                        }
                        else if (matchSource14592.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Component>))
                        {
                            matchValues14592 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Component>)(object)matchSource14592);
                        }
                        else
                        {
                            foreach (var value in (global::System.Collections.Generic.IEnumerable<global::BEngine.Component>)matchSource14592)
                            {
                                if (value.GetType() == component.GetType() && !assigned.Contains(value))
                                    return value;
                            }
                    
                            return default!;
                        }
                    
                        foreach (var value in matchValues14592)
                        {
                            if (value.GetType() == component.GetType() && !assigned.Contains(value))
                                return value;
                        }
                    
                        return default!;
                    }
                    assigned.Add(added);
                    Link(component, added);
                }
            }
        }
        foreach (var original in originals)
        {
            var copy = (GameObject)map(original);
            copy.transform.SetParent((Transform)map(original.transform.parent!), false);
            copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex());
            copyValues(original, copy, map);
            foreach (var component in original.components) copyValues(component, map(component), map);
            if (original.PrefabInstance is { } nested)
            {
                var ids = BuildIndex17147(originals.SelectMany(value => value.components.Cast<BObject>().Prepend(value)), null);
                [global::BEngine.SkipLoopGuard]
                global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid> BuildIndex17147(global::System.Collections.Generic.IEnumerable<global::BEngine.BObject> indexSource17147, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer17147)
                {
                    ArgumentNullException.ThrowIfNull(indexSource17147, "source");
                    var knownCount = indexSource17147.TryGetNonEnumeratedCount(out var capacity);
                    if (knownCount && capacity == 0)
                        return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(indexComparer17147);
                    global::System.ReadOnlySpan<global::BEngine.BObject> values;
                    if (knownCount && indexSource17147 is global::BEngine.BObject[] array)
                        values = array;
                    else if (knownCount && indexSource17147 is global::System.Collections.Generic.List<global::BEngine.BObject> list)
                        values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                    else
                    {
                        var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(capacity, indexComparer17147);
                        foreach (var item in indexSource17147)
                            result.Add(IndexKey17147(item), IndexValue17147(item));
                        return result;
                    }
                
                    {
                        var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(values.Length, indexComparer17147);
                        foreach (var item in values)
                            result.Add(IndexKey17147(item), IndexValue17147(item));
                        return result;
                    }
                
                    global::System.Guid IndexKey17147(global::BEngine.BObject value) => value.Id;
                    global::System.Guid IndexValue17147(global::BEngine.BObject value) => map(value).Id;
                }
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

    private static void RestoreHierarchy(EditorPrefabEditSession context, Action<BObject, BObject, Func<BObject, BObject>> copyValues)
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
            {
                var __filteredSource19519 = source.components;
                ArgumentNullException.ThrowIfNull(__filteredSource19519, "source");
                foreach (var sourceComponent in __filteredSource19519)
                {
                    if (!(sourceComponent is not Transform))
                        continue;
                    Component component;
                    if (context.TryLive(sourceComponent, out var current) && current is Component candidate && candidate.GetType() == sourceComponent.GetType())
                        component = candidate;
                    else
                    {
                        component = Matches23282() ?? live.AddComponent(sourceComponent.GetType());
                        [global::BEngine.SkipLoopGuard]
                        global::BEngine.Component? Matches23282()
                        {
                            var matchSource23282 = live.components;
                            ArgumentNullException.ThrowIfNull(matchSource23282, "source");
                            global::System.ReadOnlySpan<global::BEngine.Component> matchValues23282;
                            if (matchSource23282.GetType() == typeof(global::BEngine.Component[]))
                            {
                                matchValues23282 = (global::BEngine.Component[])(object)matchSource23282;
                            }
                            else if (matchSource23282.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Component>))
                            {
                                matchValues23282 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Component>)(object)matchSource23282);
                            }
                            else
                            {
                                foreach (var candidate in (global::System.Collections.Generic.IEnumerable<global::BEngine.Component>)matchSource23282)
                                {
                                    if (candidate is not Transform && candidate.GetType() == sourceComponent.GetType() && !desiredComponents.Contains(candidate) && !context.SourceIds.Values.Contains(candidate.Id))
                                        return candidate;
                                }
                        
                                return default!;
                            }
                        
                            foreach (var candidate in matchValues23282)
                            {
                                if (candidate is not Transform && candidate.GetType() == sourceComponent.GetType() && !desiredComponents.Contains(candidate) && !context.SourceIds.Values.Contains(candidate.Id))
                                    return candidate;
                            }
                        
                            return default!;
                        }
                        component.Id = context.SourceIds[sourceComponent.Id];
                        context.AddMapping(sourceComponent, component);
                    }
            
                    desiredComponents.Add(component);
                    Link(sourceComponent, component);
                }
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
            copyValues(source, live, context.ToLive);
            foreach (var component in source.components) copyValues(component, context.ToLive(component), context.ToLive);
            if (!ReferenceEquals(live, context.Root)) live.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
            live.PrefabInstance = source.PrefabInstance is { } nested && !ReferenceEquals(source, context.Contents)
                ? PrefabResolver.RemapReference(nested, context.SourceIds) : null;
        }
        context.Root.PrefabInstance = context.PreserveInstanceMetadata
            ? AssetSnapshotClone.Clone(context.Contents.PrefabInstance!) : new PrefabReferenceData
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


    public static EditorPrefabEditSession Open(GameObject root, PrefabAsset source) => new(root, source);

    public BObject SourceObject(Guid id) => OpenSourceObjects[id];

    public Component AddSourceComponent(Component instance)
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(instance);
        var owner = (GameObject)Source(instance.gameObject);
        var added = owner.AddComponent(instance.GetType()); added.Id = instance.Id;
        AddMapping(added, instance);
        instance.PrefabAssetId = Asset.assetId; instance.PrefabSourceId = added.Id;
        return added;
    }

    public Component RestoreInstanceComponent(Component source, GameObject owner)
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(owner);
        if (!ReferenceEquals(SourceObject(source.Id), source) || !ReferenceEquals(ToLive(source.gameObject), owner))
            throw new ArgumentException("The component and owner do not belong to this source/instance mapping.");
        var restored = owner.AddComponent(source.GetType());
        restored.Id = SourceIds.GetValueOrDefault(source.Id, Guid.NewGuid());
        restored.PrefabAssetId = source.PrefabAssetId ?? Asset.assetId;
        restored.PrefabSourceId = source.PrefabSourceId ?? source.Id;
        AddMapping(source, restored);
        return restored;
    }

    public void CopySubtree(GameObject root, bool toSource, Action<BObject, BObject, Func<BObject, BObject>> copyValues)
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(copyValues);
        CopySubtree(this, root, toSource, copyValues);
    }

    public void RestoreHierarchy(Action<BObject, BObject, Func<BObject, BObject>> copyValues)
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(copyValues);
        RestoreHierarchy(this, copyValues);
    }

    /// <summary>Advances inherited baselines after the caller successfully saves its source document.</summary>
    public void AcceptSourceChanges()
    {
        EnsureOpen(); AcceptChanges(Root, new(_before), new(MapSource()));
    }

    /// <summary>Captures a detached hierarchy without exposing the internal persistent graph representation.</summary>
    public static SerializedHierarchy CaptureHierarchy(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new(PrefabAssetOperations.Traverse(root).Select(item => SceneAssetSerialization.FromGameObject(item)).ToList());
    }

    /// <summary>Merges accepted instance edits into each containing prefab's inherited baseline.</summary>
    public static void AcceptChanges(GameObject root, SerializedHierarchy before, SerializedHierarchy after)
    {
        ArgumentNullException.ThrowIfNull(root);
        for (var current = root; current is not null; current = current.transform.parent?.gameObject)
            if (current.PrefabInstance is { } reference)
                reference.Baseline = PrefabResolver.Merge(before.Data, after.Data, reference.Baseline);
    }

    public static bool HasInstanceReference(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root); return root.PrefabInstance is not null;
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; BObject.DestroyImmediate(Contents);
    }
}
