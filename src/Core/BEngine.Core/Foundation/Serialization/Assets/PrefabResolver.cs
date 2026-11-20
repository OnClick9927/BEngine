using System.Security.Cryptography;
using BEngine.DependencyInjection;
namespace BEngine.Serialization;

internal static class PrefabResolver
{
    private static IPrefabResolutionBackend Backend => RuntimeServiceContributions.GetDefault<IPrefabResolutionBackend>()
        ?? throw new InvalidOperationException("No source prefab resolver is registered. Supply compiled prefab data or register a resolver.");
    internal static PrefabAssetData Resolve(PrefabAsset asset)
    {
        if (asset.Data.BasePrefab is not null || asset.Data.Instances.Count != 0) return Backend.Resolve(asset).Data;
        var data = AssetSnapshotClone.Clone(asset.Data);
        AssetDataValidation.ValidatePrefab(data);
        return data;
    }
    internal static PrefabAsset Load(string reference) => Backend.Load(reference);
    internal static string Reference(PrefabAsset asset) =>
        RuntimeServiceContributions.GetDefault<IPrefabResolutionBackend>()?.Reference(asset) ??
        (!string.IsNullOrEmpty(asset.guid) ? $"guid:{asset.guid}" : asset.assetPath);
    internal static List<GameObjectData> Merge(IReadOnlyList<GameObjectData> baseline, IReadOnlyList<GameObjectData> current, IReadOnlyList<GameObjectData> source) =>
        Backend.Merge(new(baseline), new(current), new(source)).Data;
    internal static void ResolveReferences(List<GameObjectData> objects, List<PrefabReferenceData> references)
    {
        if (references.Count > 0) Backend.ResolveReferences(new(objects), new(references));
    }
    internal static PrefabAssetData Map(PrefabAssetData source, PrefabReferenceData reference, bool connected)
    {
        if (!reference.ObjectIds.TryGetValue(source.Root, out var root) || root != reference.Root)
            throw new InvalidDataException("Prefab reference has an invalid root identity map.");
        foreach (var id in Ids(source.GameObjects))
            if (!reference.ObjectIds.ContainsKey(id)) reference.ObjectIds[id] = DerivedId(reference.Root, id);
        return new PrefabAssetData
        {
            Id = source.Id,
            Name = source.Name,
            Root = reference.Root,
            GameObjects = ProjectItems2523(),
            Instances = Gather2588(source.Instances)
        };
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.List<global::BEngine.Serialization.PrefabReferenceData> Gather2588(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.PrefabReferenceData> items2588)
        {
            ArgumentNullException.ThrowIfNull(items2588, "source");
            global::BEngine.Serialization.PrefabReferenceData Evaluate2588(global::BEngine.Serialization.PrefabReferenceData item) => RemapReference(item, reference.ObjectIds);
            global::System.ReadOnlySpan<global::BEngine.Serialization.PrefabReferenceData> values;
            if (items2588.GetType() == typeof(global::BEngine.Serialization.PrefabReferenceData[]))
                values = (global::BEngine.Serialization.PrefabReferenceData[])items2588;
            else if (items2588.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Serialization.PrefabReferenceData>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Serialization.PrefabReferenceData>)items2588);
            else
            {
                return global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(items2588, Evaluate2588));
            }

            var result = new global::System.Collections.Generic.List<global::BEngine.Serialization.PrefabReferenceData>(values.Length);
            global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(result, values.Length);
            var destination = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result);
            for (var index = 0; index < values.Length; index++)
                destination[index] = Evaluate2588(values[index]);
            return result;
        }
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData> ProjectItems2523()
        {
            var projectionSource2523 = source.GameObjects;
            ArgumentNullException.ThrowIfNull(projectionSource2523, "source");
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> projectionValues2523 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource2523);
            var projectionOutput2523 = new global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>(projectionValues2523.Length);
            global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(projectionOutput2523, projectionValues2523.Length);
            var projectionTarget2523 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionOutput2523);
            var projectionIndex2523 = 0;
            foreach (var item in projectionValues2523)
            {
                projectionTarget2523[projectionIndex2523++] = PrefabAssetOperations.CloneForInstance(item, source.Id, reference.ObjectIds, connected);
            }
        
            return projectionOutput2523;
        }
    }

    internal static PrefabReferenceData RemapReference(PrefabReferenceData reference, IReadOnlyDictionary<Guid, Guid> ids) => new()
    {
        Asset = reference.Asset,
        Root = ids.GetValueOrDefault(reference.Root, reference.Root),
        ObjectIds = reference.ObjectIds.ToDictionary(pair => pair.Key, pair => ids.GetValueOrDefault(pair.Value, pair.Value)),
        Baseline = reference.Baseline.Select(item => PrefabAssetOperations.Remap(item, ids)).ToList()
    };

    internal static IEnumerable<Guid> Ids(IEnumerable<GameObjectData> objects) =>
        PrefabHierarchyTopology.Ids(objects);

    internal static List<GameObjectData> Reachable(IEnumerable<GameObjectData> objects, Guid root) =>
        PrefabHierarchyTopology.Reachable(objects, root);

    internal static int Depth(Guid root, List<GameObjectData> objects) =>
        PrefabHierarchyTopology.Depth(root, objects);

    private static Guid DerivedId(Guid instance, Guid source)
    {
        Span<byte> input = stackalloc byte[32]; instance.TryWriteBytes(input); source.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
