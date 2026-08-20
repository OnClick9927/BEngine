namespace BEngine.Documents;

public static class ProjectSettingsMigration
{
    public static bool Normalize(ProjectSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = false;
        if (document.Version == 1)
        {
            document.Version = 2;
            changed = true;
        }

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

        document.SortingLayers ??= [];
        var supplied = document.SortingLayers;
        if (supplied.Any(layer => !SortingLayer.IsValid(layer.Value)) ||
            supplied.Select(layer => layer.Value).Distinct().Count() != supplied.Count ||
            supplied.Where(layer => !string.IsNullOrWhiteSpace(layer.Name))
                .Select(layer => layer.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            supplied.Count(layer => !string.IsNullOrWhiteSpace(layer.Name)))
            return changed;

        var byValue = supplied.ToDictionary(layer => layer.Value);
        var reservedNames = supplied.Where(layer => !string.IsNullOrWhiteSpace(layer.Name))
            .Select(layer => layer.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<SortingLayerDocument>(SortingLayer.MaximumIndex);
        foreach (var definition in SortingLayerRegistry.CreateDefaults())
        {
            if (byValue.TryGetValue(definition.Value, out var existing) &&
                !string.IsNullOrWhiteSpace(existing.Name))
            {
                normalized.Add(new SortingLayerDocument
                {
                    Value = definition.Value,
                    Name = existing.Name.Trim()
                });
                continue;
            }
            var name = UniqueDefaultName(definition, reservedNames);
            reservedNames.Add(name);
            normalized.Add(new SortingLayerDocument { Value = definition.Value, Name = name });
        }

        if (supplied.Count != normalized.Count ||
            !supplied.Select(layer => (layer.Value, (layer.Name ?? string.Empty).Trim()))
                .SequenceEqual(normalized.Select(layer => (layer.Value, layer.Name))))
        {
            document.SortingLayers = normalized;
            changed = true;
        }
        return changed;
    }

    private static string UniqueDefaultName(
        SortingLayerDefinition definition,
        ISet<string> reservedNames)
    {
        if (!reservedNames.Contains(definition.Name)) return definition.Name;
        var index = definition.Index;
        var candidate = $"{definition.Name} 2^{index}";
        for (var suffix = 2; reservedNames.Contains(candidate); suffix++)
            candidate = $"{definition.Name} 2^{index} ({suffix})";
        return candidate;
    }
}
