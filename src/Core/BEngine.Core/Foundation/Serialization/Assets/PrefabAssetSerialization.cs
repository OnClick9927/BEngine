using BEngine.Serialization;

namespace BEngine.Serialization;

internal static class PrefabAssetSerialization
{
    private static IPrefabDocumentCodec Codec => DependencyInjection.RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>() ?? throw new InvalidOperationException("No prefab document codec is registered.");
    internal static PrefabAsset Load(string path) => Codec.Load(path);
    internal static PrefabAsset Deserialize(string text, string sourcePath = "") => Codec.Deserialize(text, sourcePath);
    internal static string Serialize(PrefabAsset prefab) => Codec.Serialize(prefab);
    internal static string Serialize(GameObject root, Guid? assetId = null) => Codec.Serialize(root, assetId);
    internal static void Save(PrefabAsset prefab, string path) => Codec.Save(prefab, path);
    internal static PrefabAsset Save(GameObject root, string path, Guid? assetId = null) => Codec.Save(root, path, assetId);

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
        return BuildIndex3192(PrefabAssetOperations.Traverse(root).SelectMany(item => item.components.Cast<BObject>().Prepend(item)), null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid> BuildIndex3192(global::System.Collections.Generic.IEnumerable<global::BEngine.BObject> indexSource3192, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer3192)
        {
            ArgumentNullException.ThrowIfNull(indexSource3192, "source");
            var knownCount = indexSource3192.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(indexComparer3192);
            global::System.ReadOnlySpan<global::BEngine.BObject> values;
            if (knownCount && indexSource3192 is global::BEngine.BObject[] array)
                values = array;
            else if (knownCount && indexSource3192 is global::System.Collections.Generic.List<global::BEngine.BObject> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(capacity, indexComparer3192);
                foreach (var item in indexSource3192)
                    result.Add(IndexKey3192(item), IndexValue3192(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Guid>(values.Length, indexComparer3192);
                foreach (var item in values)
                    result.Add(IndexKey3192(item), IndexValue3192(item));
                return result;
            }
        
            global::System.Guid IndexKey3192(global::BEngine.BObject item) => item.Id;
            global::System.Guid IndexValue3192(global::BEngine.BObject item) => sourceIds?.GetValueOrDefault(item.Id, item.Id) ?? (item.PrefabAssetId == root.PrefabAssetId ? item.PrefabSourceId : null) ?? item.Id;
        }
    }

    internal static PrefabAsset SaveVariant(GameObject root, PrefabAsset basePrefab, string path, Guid? assetId = null) =>
        (BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<IPrefabAuthoringBackend>()
            ?? throw new InvalidOperationException("No source prefab authoring service is registered.")).SaveVariant(root, basePrefab, path, assetId);

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

    internal static Guid? TryReadAssetId(string path) => Codec.ReadAssetId(path);
}
