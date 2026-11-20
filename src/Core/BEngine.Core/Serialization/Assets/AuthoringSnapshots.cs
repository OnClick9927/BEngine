using System.Text.Json;
using System.Text.Json.Nodes;

namespace BEngine.Serialization;

/// <summary>An owned, editable object-graph snapshot. It never exposes live objects or internal DTOs.</summary>
public readonly partial struct SerializedHierarchy
{
    private readonly List<GameObjectData>? _objects;
    internal List<GameObjectData> Data => _objects ?? throw new InvalidOperationException("The hierarchy snapshot is uninitialized.");
    internal SerializedHierarchy(IReadOnlyList<GameObjectData> objects) => _objects = objects as List<GameObjectData> ?? objects.ToList();
    public int Count => Data.Count;
    public SerializedHierarchy Clone() => new(AssetSnapshotClone.Clone(Data));
    public SerializedHierarchy Reachable(Guid root) => new(PrefabHierarchyTopology.Reachable(Data, root));
    public int Depth(Guid root) => PrefabHierarchyTopology.Depth(root, Data);
    public bool Contains(Guid id)
    {
        foreach (var item in Data) if (item.Id == id) return true;
        return false;
    }
    public HashSet<Guid> CollectObjectIds()
    {
        var ids = new HashSet<Guid>(Data.Count);
        foreach (var item in Data) ids.Add(item.Id);
        return ids;
    }
    public Guid? ParentOf(Guid id) => Find(id).Parent;
    public void SetParent(Guid id, Guid? parent) => Find(id).Parent = parent;
    public void Replace(SerializedHierarchy removed, SerializedHierarchy replacement)
    {
        var ids = new HashSet<Guid>();
        foreach (var item in removed.Data) ids.Add(item.Id);
        var additions = replacement.Data;
        if (ReferenceEquals(Data, additions)) additions = new(additions);
        Data.RemoveAll(item => ids.Contains(item.Id));
        Data.AddRange(additions);
    }
    /// <summary>The versioned object-graph interchange representation used for custom merge policies.</summary>
    public JsonNode ToJson() => JsonSerializer.SerializeToNode(Data)!;
    public static SerializedHierarchy FromJson(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new(node.Deserialize<List<GameObjectData>>() ?? throw new InvalidDataException("An object graph is required."));
    }
    private GameObjectData Find(Guid id)
    {
        GameObjectData? found = null;
        foreach (var item in Data)
            if (item.Id == id)
            {
                if (found is not null) throw new InvalidOperationException("The object graph contains duplicate identities.");
                found = item;
            }
        return found ?? throw new InvalidOperationException("The object is absent from the snapshot.");
    }
}

/// <summary>Editable reference data owned by a detached document, including its last inherited snapshot.</summary>
public readonly struct PrefabReferenceSnapshot
{
    private readonly PrefabReferenceData? _reference;
    internal PrefabReferenceData Data => _reference ?? throw new InvalidOperationException("The prefab reference is uninitialized.");
    internal PrefabReferenceSnapshot(PrefabReferenceData reference) => _reference = reference;
    public string Asset => Data.Asset;
    public Guid Root => Data.Root;
    public IReadOnlyDictionary<Guid, Guid> ObjectIds => Data.ObjectIds;
    public SerializedHierarchy Baseline => new(Data.Baseline);
    public void SetBaseline(SerializedHierarchy baseline) => Data.Baseline = baseline.Data;
    public PrefabReferenceSnapshot Clone() => new(AssetSnapshotClone.Clone(Data));
    public static PrefabReferenceSnapshot Create(string asset, Guid root, IReadOnlyDictionary<Guid, Guid> objectIds)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(objectIds);
        return new(new PrefabReferenceData { Asset = asset, Root = root, ObjectIds = new(objectIds) });
    }
}

/// <summary>A document's references. Iteration wraps existing records by value and allocates no nodes.</summary>
public readonly struct PrefabReferenceSet
{
    private readonly List<PrefabReferenceData>? _references;
    internal List<PrefabReferenceData> Data => _references ?? throw new InvalidOperationException("The reference set is uninitialized.");
    internal PrefabReferenceSet(List<PrefabReferenceData> references) => _references = references;
    public int Count => Data.Count;
    public PrefabReferenceSnapshot? Find(Guid root)
    {
        foreach (var reference in Data) if (reference.Root == root) return new(reference);
        return null;
    }
    public void Add(PrefabReferenceSnapshot reference) => Data.Add(reference.Data);
    public void Remove(Guid root) => Data.RemoveAll(reference => reference.Root == root);
    public void RemoveWhere(Predicate<PrefabReferenceSnapshot> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Data.RemoveAll(reference => predicate(new(reference)));
    }
    public Enumerator GetEnumerator() => new(Data.GetEnumerator());
    public struct Enumerator
    {
        private List<PrefabReferenceData>.Enumerator _enumerator;
        internal Enumerator(List<PrefabReferenceData>.Enumerator enumerator) => _enumerator = enumerator;
        public PrefabReferenceSnapshot Current => new(_enumerator.Current);
        public bool MoveNext() => _enumerator.MoveNext();
        public void Dispose() => _enumerator.Dispose();
    }
}

