namespace BEngine;

public static class SortingLayerRegistry
{
    private static SortingLayerDefinition[] _layers = CreateDefaults();
    private static IReadOnlyList<SortingLayerDefinition> _view = Array.AsReadOnly(_layers);
    private static int _version;

    public static int version
    {
        get => _version;
    }

    public static IReadOnlyList<SortingLayerDefinition> layers
    {
        get => _view;
    }

    public static string NameOf(ulong value)
    {
        SortingLayer.Validate(value);
        return _layers[SortingLayer.IndexOf(value) - SortingLayer.MinimumIndex].Name;
    }

    public static void Configure(IEnumerable<SortingLayerDefinition>? definitions)
    {
        var values = CreateDefaults();
        foreach (var definition in definitions ?? [])
        {
            SortingLayer.Validate(definition.Value);
            var name = definition.Name?.Trim() ?? string.Empty;
            if (name.Length == 0) continue;
            values[definition.Index - SortingLayer.MinimumIndex] = definition with { Name = name };
        }
        var duplicates = values.Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
            throw new InvalidDataException($"Sorting layer name '{duplicates.Key}' is not unique.");
        _layers = values;
        _view = Array.AsReadOnly(_layers);
        _version++;
    }

    internal static SortingLayerDefinition[] CreateDefaults()
    {
        var result = new SortingLayerDefinition[SortingLayer.MaximumIndex];
        for (var index = SortingLayer.MinimumIndex; index <= SortingLayer.MaximumIndex; index++)
        {
            var name = index switch
            {
                SortingLayer.MinimumIndex => "World",
                SortingLayer.UiBaseIndex => "UI",
                > SortingLayer.UiBaseIndex => $"UI Overlay {index - SortingLayer.UiBaseIndex}",
                _ => $"World {index}"
            };
            result[index - SortingLayer.MinimumIndex] =
                new SortingLayerDefinition(SortingLayer.FromIndex(index), name);
        }
        return result;
    }
}
