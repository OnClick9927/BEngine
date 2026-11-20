namespace BEngine.Serialization;

internal static class SourcePrefabResolver
{
    internal static PrefabDocumentSnapshot Resolve(PrefabAsset asset) => Resolve(asset, []);

    private static PrefabDocumentSnapshot Resolve(PrefabAsset asset, HashSet<Guid> stack)
    {
        if (stack.Count >= 64 || !stack.Add(asset.assetId))
            throw new InvalidDataException($"Cyclic or excessively deep prefab reference: {asset.assetPath}");
        try
        {
            var document = PrefabDocumentSnapshot.CopyFrom(asset);
            if (document.BaseReference is { } reference)
            {
                var source = Resolve(Load(reference.Asset), stack);
                var mapped = PrefabDocumentSnapshot.Map(source, reference, connected: false);
                document.SetObjects(PrefabMerge.Merge(reference.Baseline, document.Objects, mapped.Objects).Reachable(document.Root));
                MergeReferences(document.References, mapped.References, document.Objects);
                reference.SetBaseline(mapped.Objects.Clone());
            }
            ResolveReferences(document.Objects, document.References, stack);
            document.Validate();
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
        var physical = asset.sourcePath;
        var files = RuntimeServices.GetRequiredService<Platform.FileUtility>();
        var imported = !files.FileExists(physical) || files.FileExists(physical + ".meta");
        return imported && !string.IsNullOrEmpty(asset.guid) ? $"guid:{asset.guid}" : asset.assetPath;
    }

    internal static void ResolveReferences(SerializedHierarchy objects, PrefabReferenceSet references) =>
        ResolveReferences(objects, references, []);

    private static void ResolveReferences(SerializedHierarchy objects, PrefabReferenceSet references, HashSet<Guid> stack)
    {
        var pending = new List<Guid>(references.Count);
        foreach (var reference in references) pending.Add(reference.Root);
        pending = pending.OrderBy(objects.Depth).ToList();
        var visited = new HashSet<Guid>();
        for (var index = 0; index < pending.Count; index++)
        {
            var rootId = pending[index];
            if (!visited.Add(rootId) || references.Find(rootId) is not { } reference || !objects.Contains(rootId)) continue;
            var source = Resolve(Load(reference.Asset), stack);
            var mapped = PrefabDocumentSnapshot.Map(source, reference, connected: true);
            mapped.Objects.SetParent(rootId, objects.ParentOf(rootId));
            var current = objects.Reachable(rootId);
            var merged = PrefabMerge.Merge(reference.Baseline, current, mapped.Objects).Reachable(rootId);
            objects.Replace(current, merged);
            reference.SetBaseline(mapped.Objects.Clone());
            MergeReferences(references, mapped.References, objects);
            foreach (var nested in mapped.References)
                if (!visited.Contains(nested.Root)) pending.Add(nested.Root);
        }
        var ids = objects.CollectObjectIds();
        references.RemoveWhere(reference => !ids.Contains(reference.Root));
    }

    private static void MergeReferences(PrefabReferenceSet current, PrefabReferenceSet inherited, SerializedHierarchy objects)
    {
        var ids = objects.CollectObjectIds();
        current.RemoveWhere(reference => !ids.Contains(reference.Root));
        foreach (var reference in inherited)
            if (ids.Contains(reference.Root) && current.Find(reference.Root) is null) current.Add(reference);
    }
}
