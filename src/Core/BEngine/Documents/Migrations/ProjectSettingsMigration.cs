namespace BEngine.Documents;

public static class ProjectSettingsMigration
{
    public static bool Normalize(ProjectSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = NormalizeCommon(document);
        document.SortingLayers ??= [];

        if (document.Version < 3)
        {
            document.SortingLayers = MigrateLegacyLayers(document.SortingLayers);
            document.Version = 3;
            return true;
        }

        var layers = document.SortingLayers;
        if (layers.Count < SortingLayer.BuiltInLayerCount)
        {
            var suppliedNames = layers.Select(static item => item.Name?.Trim() ?? string.Empty)
                .Where(static name => name.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in SortingLayerRegistry.CreateDefaults().Skip(layers.Count))
            {
                var name = UniqueName(definition.Name, suppliedNames);
                suppliedNames.Add(name);
                layers.Add(new SortingLayerDocument
                {
                    Value = (ulong)(layers.Count + 1),
                    Name = name,
                    BuiltIn = true,
                    IsUi = definition.IsUi,
                    BuiltInId = definition.BuiltInId
                });
            }
            changed = true;
        }

        changed |= EnsureBuiltInIdentities(layers);
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            var value = (ulong)(index + SortingLayer.MinimumIndex);
            var name = layer.Name?.Trim() ?? string.Empty;
            var isUi = layer.IsUi || layer.BuiltInId == SortingLayerRegistry.UiId;
            if (layer.Value == value && layer.Name == name && layer.IsUi == isUi) continue;
            layer.Value = value;
            layer.Name = name;
            layer.IsUi = isUi;
            changed = true;
        }
        return changed;
    }

    private static bool NormalizeCommon(ProjectSettingsDocument document)
    {
        var changed = false;
        var tags = (document.Tags ?? [])
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Prepend("Untagged")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!(document.Tags ?? []).SequenceEqual(tags, StringComparer.Ordinal))
        {
            document.Tags = tags;
            changed = true;
        }
        if (document.ScriptingDefineSymbols is null)
        {
            document.ScriptingDefineSymbols = [];
            changed = true;
        }
        return changed;
    }

    private static List<SortingLayerDocument> MigrateLegacyLayers(
        IReadOnlyList<SortingLayerDocument> supplied)
    {
        if (supplied.Count == 0)
            return SortingLayerRegistry.CreateDefaults().Select(ToDocument).ToList();

        if (supplied.Select(static item => item.Value)
            .SequenceEqual(Enumerable.Range(1, supplied.Count).Select(static index => (ulong)index)))
            return MigrateNaturalLayers(supplied);

        var legacy = supplied
            .Where(static item => item.Value >= 2 && (item.Value & (item.Value - 1)) == 0)
            .Select(static item => (LegacyIndex: LegacyIndex(item.Value), Item: item))
            .Where(static item => item.LegacyIndex is >= SortingLayer.MinimumIndex and <= SortingLayer.MaximumIndex)
            .Select(static item => (Index: SortingLayer.MapLegacyIndex(item.LegacyIndex),
                item.LegacyIndex, item.Item))
            .GroupBy(static item => item.Index)
            .ToDictionary(static group => group.Key, static group => group.First());
        if (legacy.Count == 0) return SortingLayerRegistry.CreateDefaults().Select(ToDocument).ToList();

        var count = Math.Max(SortingLayer.BuiltInLayerCount, legacy.Keys.Max());
        var reservedNames = legacy.Values.Where(static item => !string.IsNullOrWhiteSpace(item.Item.Name))
            .Select(static item => item.Item.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<SortingLayerDocument>(count);
        for (var index = SortingLayer.MinimumIndex; index <= count; index++)
        {
            var isUi = index == SortingLayer.LegacyMappedUiIndex || index >= 60;
            var fallback = index <= SortingLayer.BuiltInLayerCount
                ? SortingLayerRegistry.CreateDefaults()[index - 1].Name
                : isUi
                    ? $"UI Overlay {index - 59}"
                    : $"World {index}";
            var hasExisting = legacy.TryGetValue(index, out var existing);
            var name = hasExisting && !string.IsNullOrWhiteSpace(existing.Item.Name)
                ? existing.Item.Name.Trim()
                : UniqueName(fallback, reservedNames);
            reservedNames.Add(name);
            var builtInId = hasExisting ? BuiltInIdForLegacyIndex(existing.LegacyIndex) :
                BuiltInIdForNaturalDefault(index);
            result.Add(new SortingLayerDocument
            {
                Value = (ulong)index,
                Name = name,
                BuiltIn = builtInId.Length > 0,
                IsUi = isUi,
                BuiltInId = builtInId
            });
        }
        return result;
    }

    private static List<SortingLayerDocument> MigrateNaturalLayers(
        IReadOnlyList<SortingLayerDocument> supplied)
    {
        var defaults = SortingLayerRegistry.CreateDefaults();
        var count = Math.Max(SortingLayer.BuiltInLayerCount, supplied.Count);
        var result = new List<SortingLayerDocument>(count);
        var names = supplied.Where(static item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(static item => item.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var offset = 0; offset < count; offset++)
        {
            var existing = offset < supplied.Count ? supplied[offset] : null;
            var fallback = offset < defaults.Length ? defaults[offset].Name : $"Layer {offset + 1}";
            var name = existing is not null && !string.IsNullOrWhiteSpace(existing.Name)
                ? existing.Name.Trim()
                : UniqueName(fallback, names);
            names.Add(name);
            result.Add(new SortingLayerDocument
            {
                Value = (ulong)(offset + SortingLayer.MinimumIndex),
                Name = name,
                BuiltIn = offset < SortingLayer.BuiltInLayerCount || existing?.BuiltIn == true,
                IsUi = existing?.IsUi == true || offset == SortingLayer.LegacyMappedUiIndex - 1,
                BuiltInId = existing?.BuiltInId is { Length: > 0 } id
                    ? id
                    : offset < defaults.Length ? defaults[offset].BuiltInId : string.Empty
            });
        }
        return result;
    }

    private static SortingLayerDocument ToDocument(SortingLayerDefinition definition) => new()
    {
        Value = definition.Value,
        Name = definition.Name,
        BuiltIn = definition.IsBuiltIn,
        IsUi = definition.IsUi,
        BuiltInId = definition.BuiltInId
    };

    private static bool EnsureBuiltInIdentities(IReadOnlyList<SortingLayerDocument> layers)
    {
        var changed = false;
        var defaults = SortingLayerRegistry.CreateDefaults();
        foreach (var layer in layers)
        {
            var id = layer.BuiltInId?.Trim() ?? string.Empty;
            if (layer.BuiltInId == id) continue;
            layer.BuiltInId = id;
            changed = true;
        }
        var used = layers.Where(static layer => !string.IsNullOrEmpty(layer.BuiltInId))
            .Select(static layer => layer.BuiltInId).ToHashSet(StringComparer.Ordinal);
        var missing = new Queue<string>(defaults.Select(static layer => layer.BuiltInId)
            .Where(id => !used.Contains(id)));
        foreach (var layer in layers.Where(static layer => layer.BuiltIn &&
                     string.IsNullOrEmpty(layer.BuiltInId)))
            if (missing.TryDequeue(out var id))
            {
                layer.BuiltInId = id;
                changed = true;
            }
        for (var index = 0; missing.Count > 0 && index < layers.Count; index++)
        {
            var layer = layers[index];
            if (!string.IsNullOrEmpty(layer.BuiltInId)) continue;
            layer.BuiltIn = true;
            layer.BuiltInId = missing.Dequeue();
            changed = true;
        }
        return changed;
    }

    private static string BuiltInIdForNaturalDefault(int index) => index switch
    {
        1 => SortingLayerRegistry.DefaultId,
        2 => SortingLayerRegistry.TransparentFxId,
        3 => SortingLayerRegistry.IgnoreRaycastId,
        4 => SortingLayerRegistry.WaterId,
        5 => SortingLayerRegistry.UiId,
        _ => string.Empty
    };

    private static string BuiltInIdForLegacyIndex(int index) => index switch
    {
        1 => SortingLayerRegistry.DefaultId,
        2 => SortingLayerRegistry.TransparentFxId,
        3 => SortingLayerRegistry.IgnoreRaycastId,
        4 => SortingLayerRegistry.WaterId,
        59 => SortingLayerRegistry.UiId,
        _ => string.Empty
    };

    private static string UniqueName(string preferred, ISet<string> reserved)
    {
        if (!reserved.Contains(preferred)) return preferred;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{preferred} {suffix}";
            if (!reserved.Contains(candidate)) return candidate;
        }
    }

    private static int LegacyIndex(ulong value) =>
        System.Numerics.BitOperations.TrailingZeroCount(value);
}
