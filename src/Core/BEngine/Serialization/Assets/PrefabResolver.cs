using System.Security.Cryptography;

namespace BEngine.Serialization;

internal static class PrefabResolver
{
    internal static PrefabAssetData Resolve(PrefabAsset asset) => Resolve(asset, []);

    private static PrefabAssetData Resolve(PrefabAsset asset, HashSet<Guid> stack)
    {
        if (stack.Count >= 64 || !stack.Add(asset.assetId))
            throw new InvalidDataException($"Cyclic or excessively deep prefab reference: {asset.assetPath}");
        try
        {
            var document = PrefabMerge.Clone(asset.Data);
            if (document.BasePrefab is { } baseReference)
            {
                var source = Resolve(Load(baseReference.Asset), stack);
                var mapped = Map(source, baseReference, connected: false);
                document.GameObjects = Reachable(PrefabMerge.Merge(baseReference.Baseline, document.GameObjects, mapped.GameObjects), document.Root);
                MergeReferences(document.Instances, mapped.Instances, document.GameObjects);
                baseReference.Baseline = PrefabMerge.Clone(mapped.GameObjects);
            }
            ResolveReferences(document.GameObjects, document.Instances, stack);
            AssetDataValidation.ValidatePrefab(document);
            return document;
        }
        finally { stack.Remove(asset.assetId); }
    }

    internal static PrefabAsset Load(string reference) =>
        (reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)
            ? BAsset.LoadByGuid<PrefabAsset>(reference[5..]) ?? BAsset.Load<PrefabAsset>(reference)
            : BAsset.Load<PrefabAsset>(reference)) ??
        throw new InvalidDataException($"Referenced prefab is missing: {reference}");

    internal static string Reference(PrefabAsset asset)
    {
        // Documents can be authored before import has created metadata. Their generated
        // in-memory GUID is not yet a resolvable asset address.
        var physical = asset.sourcePath;
        var imported = !RuntimeServiceAccess.FileSystem.FileExists(physical) ||
            RuntimeServiceAccess.FileSystem.FileExists(physical + ".meta");
        return imported && !string.IsNullOrEmpty(asset.guid) ? $"guid:{asset.guid}" : asset.assetPath;
    }

    internal static void ResolveReferences(List<GameObjectData> objects, List<PrefabReferenceData> references) =>
        ResolveReferences(objects, references, []);

    private static void ResolveReferences(List<GameObjectData> objects, List<PrefabReferenceData> references, HashSet<Guid> stack)
    {
        // Parent instances run before their nested instances. Nested roots may be added or
        // removed by a newer source; work from a queue and validate the hierarchy each time.
        var pending = references.OrderBy(item => Depth(item.Root, objects)).Select(item => item.Root).ToList();
        var visited = new HashSet<Guid>();
        for (var index = 0; index < pending.Count; index++)
        {
            var rootId = pending[index];
            if (!visited.Add(rootId)) continue;
            var reference = references.FirstOrDefault(item => item.Root == rootId);
            var root = objects.FirstOrDefault(item => item.Id == rootId);
            if (reference is null || root is null) continue;
            var source = Resolve(Load(reference.Asset), stack);
            var mapped = Map(source, reference, connected: true);
            var inheritedRoot = mapped.GameObjects.Single(item => item.Id == rootId);
            inheritedRoot.Parent = root.Parent;
            var current = Reachable(objects, rootId);
            var merged = Reachable(PrefabMerge.Merge(reference.Baseline, current, mapped.GameObjects), rootId);
            var replacedIds = current.Select(item => item.Id).ToHashSet();
            objects.RemoveAll(item => replacedIds.Contains(item.Id));
            objects.AddRange(merged);
            reference.Baseline = PrefabMerge.Clone(mapped.GameObjects);
            MergeReferences(references, mapped.Instances, objects);
            pending.AddRange(mapped.Instances.Select(item => item.Root).Where(id => !visited.Contains(id)));
        }
        var ids = objects.Select(item => item.Id).ToHashSet();
        references.RemoveAll(item => !ids.Contains(item.Root));
    }

    private static void MergeReferences(List<PrefabReferenceData> current, List<PrefabReferenceData> inherited,
        List<GameObjectData> objects)
    {
        var ids = objects.Select(item => item.Id).ToHashSet();
        current.RemoveAll(item => !ids.Contains(item.Root));
        foreach (var item in inherited)
            if (ids.Contains(item.Root) && current.All(existing => existing.Root != item.Root)) current.Add(item);
    }

    internal static PrefabAssetData Map(PrefabAssetData source, PrefabReferenceData reference, bool connected)
    {
        if (!reference.ObjectIds.TryGetValue(source.Root, out var root) || root != reference.Root)
            throw new InvalidDataException("Prefab reference has an invalid root identity map.");
        foreach (var id in Ids(source.GameObjects))
            if (!reference.ObjectIds.ContainsKey(id)) reference.ObjectIds[id] = DerivedId(reference.Root, id);
        return new PrefabAssetData
        {
            Id = source.Id, Name = source.Name, Root = reference.Root,
            GameObjects = source.GameObjects.Select(item => PrefabAssetOperations.CloneForInstance(
                item, source.Id, reference.ObjectIds, connected)).ToList(),
            Instances = source.Instances.Select(item => RemapReference(item, reference.ObjectIds)).ToList()
        };
    }

    internal static PrefabReferenceData RemapReference(PrefabReferenceData reference, IReadOnlyDictionary<Guid, Guid> ids) => new()
    {
        Asset = reference.Asset,
        Root = ids.GetValueOrDefault(reference.Root, reference.Root),
        ObjectIds = reference.ObjectIds.ToDictionary(pair => pair.Key, pair => ids.GetValueOrDefault(pair.Value, pair.Value)),
        Baseline = reference.Baseline.Select(item => PrefabAssetOperations.Remap(item, ids)).ToList()
    };

    internal static IEnumerable<Guid> Ids(IEnumerable<GameObjectData> objects) => objects.SelectMany(item =>
        new[] { item.Id, item.Transform.Id }.Concat(item.Components.Select(component => component.Id)));

    internal static List<GameObjectData> Reachable(IEnumerable<GameObjectData> objects, Guid root)
    {
        var all = objects.ToArray();
        var byId = all.ToDictionary(item => item.Id);
        var children = all.Where(item => item.Parent.HasValue).ToLookup(item => item.Parent!.Value);
        var result = new List<GameObjectData>();
        var queue = new Queue<Guid>(); queue.Enqueue(root);
        var seen = new HashSet<Guid>();
        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id)) throw new InvalidDataException("Prefab hierarchy contains a cycle.");
            var item = byId.GetValueOrDefault(id);
            if (item is null) continue;
            result.Add(item);
            foreach (var child in children[id]) queue.Enqueue(child.Id);
        }
        return result;
    }

    private static int Depth(Guid root, List<GameObjectData> objects)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(root) && objects.FirstOrDefault(item => item.Id == root)?.Parent is { } parent) root = parent;
        return seen.Count;
    }

    private static Guid DerivedId(Guid instance, Guid source)
    {
        Span<byte> input = stackalloc byte[32]; instance.TryWriteBytes(input); source.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
