namespace BEngine.Serialization;

internal static class PrefabDocumentValidation
{
    internal static void Validate(PrefabAssetData document)
    {
        if (document.Format != "BEngine.Prefab")
            throw new InvalidDataException($"Unsupported document format '{document.Format}'.");
        if (document.Version is not (2 or 3))
            throw new InvalidDataException($"Unsupported BEngine prefab version {document.Version}.");
        bool Query962()
        {
            var querySource962 = document.GameObjects;
            ArgumentNullException.ThrowIfNull(querySource962, "source");
            if (querySource962.GetType() == typeof(global::BEngine.Serialization.GameObjectData[]) || querySource962.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>))
            {
                global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> queryValues962 = querySource962.GetType() == typeof(global::BEngine.Serialization.GameObjectData[]) ? (global::BEngine.Serialization.GameObjectData[])(object)querySource962 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>)(object)querySource962);
                foreach (var item in queryValues962)
                {
                    if (!(item.Id != document.Root))
                        return false;
                }
            }
            else
            {
                foreach (var item in querySource962)
                {
                    if (!(item.Id != document.Root))
                        return false;
                }
            }
        
            return true;
        }
        
        if (document.GameObjects.Count == 0 || Query962())
            throw new InvalidDataException("Prefab document has no valid root GameObject.");
        var ids = BuildIndex2401(document.GameObjects);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.HashSet<global::System.Guid> BuildIndex2401(global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData> indexSource2401)
        {
            ArgumentNullException.ThrowIfNull(indexSource2401, "source");
            var indexComparer2401 = (global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? )null;
            var result = new global::System.Collections.Generic.HashSet<global::System.Guid>(indexComparer2401);
            foreach (var item in indexSource2401)
                result.Add(item.Id);
            return result;
        }
        if (ids.Count != document.GameObjects.Count)
            throw new InvalidDataException("Prefab document contains duplicate GameObject IDs.");
        bool __queryResult1359;
        {
            var __querySource1359 = document.GameObjects;
            ArgumentNullException.ThrowIfNull(__querySource1359, "source");
            __queryResult1359 = false;
            foreach (var __queryItem1359 in __querySource1359)
            {
                if ((__queryItem1359.Parent.HasValue && !ids.Contains(__queryItem1359.Parent.Value)))
                {
                    __queryResult1359 = true;
                    break;
                }
            }
        }
        
        if (__queryResult1359)
            throw new InvalidDataException("Prefab document contains a parent outside its hierarchy.");
        if ((((global::System.Collections.Generic.IEnumerable<global::System.Guid>)(PrefabHierarchyTopology.Ids(document.GameObjects))) switch
        {
            global::System.Guid[] { Length: 0 } => 0,
            var distinctItems3947 => distinctItems3947.ToHashSet<global::System.Guid>().Count
        }) != PrefabHierarchyTopology.Ids(document.GameObjects).Count())
            throw new InvalidDataException("Prefab contains duplicate object or component identities.");
        if (Gather4410(document.GameObjects).Parent.HasValue || PrefabHierarchyTopology.Reachable(document.GameObjects, document.Root).Count != document.GameObjects.Count)
            throw new InvalidDataException("Prefab must contain a single, acyclic hierarchy.");
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Serialization.GameObjectData Gather4410(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData> items4410)
        {
            ArgumentNullException.ThrowIfNull(items4410, "source");
            bool Evaluate4410(global::BEngine.Serialization.GameObjectData item) => item.Id == document.Root;
            global::System.ReadOnlySpan<global::BEngine.Serialization.GameObjectData> values;
            if (items4410.GetType() == typeof(global::BEngine.Serialization.GameObjectData[]))
                values = (global::BEngine.Serialization.GameObjectData[])items4410;
            else if (items4410.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Serialization.GameObjectData>)items4410);
            else
                return global::System.Linq.Enumerable.Single(items4410, Evaluate4410);
            var found = false;
            global::BEngine.Serialization.GameObjectData result = default!;
            foreach (var item in values)
            {
                if (!Evaluate4410(item))
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
    }
}
