using System.Text.Json;
using System.Text.Json.Nodes;

namespace BEngine.Serialization;

internal static class PrefabMerge
{
    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    internal static List<GameObjectData> Merge(IReadOnlyList<GameObjectData> baseline,
        IReadOnlyList<GameObjectData> current, IReadOnlyList<GameObjectData> source)
    {
        var result = MergeNode(JsonSerializer.SerializeToNode(baseline),
            JsonSerializer.SerializeToNode(current), JsonSerializer.SerializeToNode(source), "GameObjects");
        return result!.Deserialize<List<GameObjectData>>()!;
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
        var indexes = graphs.Select(graph => Index((JsonArray)graph["Members"]!, "Name")).ToArray();
        var names = indexes.SelectMany(index => index.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var parents = names.ToDictionary(name => name, name => name, StringComparer.Ordinal);
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
