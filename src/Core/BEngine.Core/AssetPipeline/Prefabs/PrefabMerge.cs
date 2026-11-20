using System.Text.Json;
using System.Text.Json.Nodes;

namespace BEngine.Serialization;

internal static class PrefabMerge
{

    internal static SerializedHierarchy Merge(SerializedHierarchy baseline,
        SerializedHierarchy current, SerializedHierarchy source)
    {
        var result = MergeNode(baseline.ToJson(), current.ToJson(), source.ToJson(), "GameObjects");
        return SerializedHierarchy.FromJson(result!);
    }

    internal static bool Equal<T>(T left, T right) =>
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(left), JsonSerializer.SerializeToNode(right));

    private static JsonNode? MergeNode(JsonNode? baseline, JsonNode? current, JsonNode? source, string property)
    {
        if (JsonNode.DeepEquals(baseline, current)) return source?.DeepClone();
        if (baseline is JsonObject oldObject && current is JsonObject instanceObject && source is JsonObject sourceObject)
        {
            // Merge connected groups of managed references together. Unrelated fields
            // still inherit, while shared/cyclic objects retain one coherent identity.
            if (property == "Graph")
                return MergeGraph(oldObject, instanceObject, sourceObject);
            var result = (JsonObject)sourceObject.DeepClone();
            foreach (var key in oldObject.Select(pair => pair.Key).Union(instanceObject.Select(pair => pair.Key)))
            {
                if (!instanceObject.ContainsKey(key)) { result.Remove(key); continue; }
                if (!oldObject.ContainsKey(key)) { result[key] = instanceObject[key]?.DeepClone(); continue; }
                result[key] = MergeNode(oldObject[key], instanceObject[key], sourceObject[key], key);
            }
            return result;
        }
        if (baseline is JsonArray oldItems && current is JsonArray instanceItems && source is JsonArray sourceItems &&
            property is "GameObjects" or "Components" or "Members")
        {
            var identity = property == "Members" ? "Name" : "Id";
            var oldMap = Index(oldItems, identity);
            var instanceMap = Index(instanceItems, identity);
            var sourceMap = Index(sourceItems, identity);
            var result = new JsonArray();
            foreach (var key in sourceMap.Keys.Union(instanceMap.Keys))
            {
                oldMap.TryGetValue(key, out var oldItem);
                instanceMap.TryGetValue(key, out var currentItem);
                sourceMap.TryGetValue(key, out var sourceItem);
                if (oldItem is not null && (currentItem is null || sourceItem is null)) continue;
                result.Add(oldItem is null ? (currentItem ?? sourceItem)?.DeepClone() :
                    MergeNode(oldItem, currentItem, sourceItem, ""));
            }
            return result;
        }
        return current?.DeepClone();
    }

    private static Dictionary<string, JsonNode?> Index(JsonArray items, string key) =>
        items.ToDictionary(item => item?[key]?.GetValue<string>() ??
            throw new InvalidDataException($"Prefab collection has no {key}."), item => item, StringComparer.Ordinal);

    private static JsonObject MergeGraph(JsonObject baseline, JsonObject current, JsonObject source)
    {
        var graphs = new[] { baseline, current, source };
        var projectionSource3445 = graphs;
        ArgumentNullException.ThrowIfNull(projectionSource3445, "source");
        var projectionResult3445 = projectionSource3445.Length == 0 ? global::System.Array.Empty<global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.Nodes.JsonNode?>>() : new global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.Nodes.JsonNode?>[projectionSource3445.Length];
        for (var projectionIndex3445 = 0; projectionIndex3445 < projectionSource3445.Length; projectionIndex3445++)
        {
            var projectionItem3445 = projectionSource3445[projectionIndex3445];
            projectionResult3445[projectionIndex3445] = Index((JsonArray)projectionItem3445["Members"]!, "Name");
        }
        
        var indexes = projectionResult3445;
        var names = ((((global::System.Collections.Generic.IEnumerable<string>)(indexes.SelectMany(index => index.Keys))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems4272, distinctComparer4272) => distinctItems4272.ToHashSet<string>(distinctComparer4272).ToArray()});
        var parents = BuildIndex4378(names, StringComparer.Ordinal);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex4378(global::System.Collections.Generic.IEnumerable<string> indexSource4378, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer4378)
        {
            ArgumentNullException.ThrowIfNull(indexSource4378, "source");
            var knownCount = indexSource4378.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer4378);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource4378 is string[] array)
                values = array;
            else if (knownCount && indexSource4378 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer4378);
                foreach (var item in indexSource4378)
                    result.Add(IndexKey4378(item), IndexValue4378(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer4378);
                foreach (var item in values)
                    result.Add(IndexKey4378(item), IndexValue4378(item));
                return result;
            }
        
            string IndexKey4378(string name) => name;
            string IndexValue4378(string name) => name;
        }
        string Root(string name) => parents[name] == name ? name : parents[name] = Root(parents[name]);
        foreach (var index in indexes)
        {
            var owners = new Dictionary<int, string>();
            foreach (var pair in index)
            foreach (var value in Values(pair.Value))
                if (value["ObjectId"]?.GetValue<int>() is > 0 and var id) owners[id] = pair.Key;
            foreach (var pair in index)
            foreach (var value in Values(pair.Value))
                if (value["ReferenceId"]?.GetValue<int>() is > 0 and var id && owners.TryGetValue(id, out var owner))
                    parents[Root(pair.Key)] = Root(owner);
        }
        var result = new JsonArray();
        var nextId = 0;
        foreach (var group in names.GroupBy(Root))
        {
            var members = group.ToHashSet(StringComparer.Ordinal);
            var portions = graphs.Select(graph => new JsonArray(((JsonArray)graph["Members"]!)
                .Where(item => members.Contains(item!["Name"]!.GetValue<string>())).Select(item => item!.DeepClone()).ToArray())).ToArray();
            foreach (var portion in portions) { var localId = 0; Renumber(portion, ref localId); }
            JsonArray merged;
            if (portions.Any(ContainsReference))
                merged = (JsonArray)(JsonNode.DeepEquals(portions[0], portions[1]) ? portions[2] : portions[1]).DeepClone();
            else merged = (JsonArray)MergeNode(portions[0], portions[1], portions[2], "Members")!;
            Renumber(merged, ref nextId);
            foreach (var item in merged) result.Add(item!.DeepClone());
        }
        return new JsonObject { ["Version"] = source["Version"]!.DeepClone(), ["Members"] = result };
    }

    private static IEnumerable<JsonObject> Values(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value.ContainsKey("Kind")) yield return value;
            foreach (var child in value)
            foreach (var nested in Values(child.Value)) yield return nested;
        }
        else if (node is JsonArray array)
            foreach (var child in array)
            foreach (var nested in Values(child)) yield return nested;
    }

    private static void Renumber(JsonNode node, ref int nextId)
    {
        var values = Values(node).ToArray();
        var ids = new Dictionary<int, int>();
        foreach (var value in values)
            if (value["ObjectId"]?.GetValue<int>() is > 0 and var id)
            {
                ids.TryAdd(id, ++nextId);
                value["ObjectId"] = nextId;
            }
        foreach (var value in values)
        {
            if (value["ReferenceId"]?.GetValue<int>() is > 0 and var reference)
                value["ReferenceId"] = ids.TryGetValue(reference, out var mapped) ? mapped :
                    throw new InvalidDataException("Prefab override contains an unresolved managed reference.");
        }
    }

    private static bool ContainsReference(JsonNode? value) => value switch
    {
        JsonObject obj => obj["Kind"]?.GetValue<string>() == "Reference" || obj.Any(pair => ContainsReference(pair.Value)),
        JsonArray array => array.Any(ContainsReference),
        _ => false
    };
}
