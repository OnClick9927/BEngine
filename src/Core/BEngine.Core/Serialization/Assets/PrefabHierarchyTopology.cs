namespace BEngine.Serialization;

internal static class PrefabHierarchyTopology
{
    internal static IEnumerable<Guid> Ids(IEnumerable<GameObjectData> objects)
    {
        ArgumentNullException.ThrowIfNull(objects, "source");
        return Enumerate(objects);
        static IEnumerable<Guid> Enumerate(IEnumerable<GameObjectData> values)
        {
            foreach (var item in values)
            {
                var id = item.Id; var transformId = item.Transform.Id;
                var components = item.Components;
                ArgumentNullException.ThrowIfNull(components, "source");
                yield return id;
                yield return transformId;
                foreach (var component in components) yield return component.Id;
            }
        }
    }

    internal static List<GameObjectData> Reachable(IEnumerable<GameObjectData> objects, Guid root)
    {
        var all = objects.ToArray();
        var byId = BuildIndex5278(all, null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.Serialization.GameObjectData> BuildIndex5278(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData> indexSource5278, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer5278)
        {
            ArgumentNullException.ThrowIfNull(indexSource5278, "source");
            var knownCount = indexSource5278.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.Serialization.GameObjectData>(indexComparer5278);
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> values;
            if (knownCount && indexSource5278 is global::BEngine.Serialization.GameObjectData[] array)
                values = array;
            else if (knownCount && indexSource5278 is global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.Serialization.GameObjectData>(capacity, indexComparer5278);
                foreach (var item in indexSource5278)
                    result.Add(IndexKey5278(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.Serialization.GameObjectData>(values.Length, indexComparer5278);
                foreach (var item in values)
                    result.Add(IndexKey5278(item), item);
                return result;
            }
        
            global::System.Guid IndexKey5278(global::BEngine.Serialization.GameObjectData item) => item.Id;
        }
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

    internal static int Depth(Guid root, List<GameObjectData> objects)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(root) && Matches5024()?.Parent is { } parent)
            root = parent;
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Serialization.GameObjectData? Matches5024()
        {
            var matchSource5024 = objects;
            ArgumentNullException.ThrowIfNull(matchSource5024, "source");
            if (matchSource5024.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>))
            {
                foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData>)matchSource5024)
                {
                    if (item.Id == root)
                        return item;
                }
        
                return default!;
            }
        
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> matchValues5024 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource5024);
            foreach (var item in matchValues5024)
            {
                if (item.Id == root)
                    return item;
            }
        
            return default!;
        }
        return seen.Count;
    }
}
