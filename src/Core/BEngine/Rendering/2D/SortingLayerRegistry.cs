namespace BEngine;

public static class SortingLayerRegistry
{
    internal const string DefaultId = "Default";
    internal const string TransparentFxId = "TransparentFX";
    internal const string IgnoreRaycastId = "IgnoreRaycast";
    internal const string WaterId = "Water";
    internal const string UiId = "UI";
    private static readonly string[] BuiltInIds =
        [DefaultId, TransparentFxId, IgnoreRaycastId, WaterId, UiId];
    private static SortingLayerDefinition[] _layers = CreateDefaults();
    private static IReadOnlyList<SortingLayerDefinition> _view = Array.AsReadOnly(_layers);
    private static int _version;

    public static int version => _version;
    public static IReadOnlyList<SortingLayerDefinition> layers => _view;

    public static bool IsDefined(ulong value) => _layers.Any(item => item.Value == value);

    public static bool IsUi(ulong value) =>
        _layers.FirstOrDefault(item => item.Value == value) is { IsUi: true };

    internal static ulong ValueOfBuiltIn(string id)
    {
        var layer = _layers.FirstOrDefault(item =>
            string.Equals(item.BuiltInId, id, StringComparison.Ordinal));
        return layer.Value != 0
            ? layer.Value
            : throw new InvalidOperationException($"The built-in Layer '{id}' is not configured.");
    }

    public static string NameOf(ulong value)
    {
        SortingLayer.Validate(value);
        return _layers.FirstOrDefault(item => item.Value == value).Name ?? string.Empty;
    }

    public static void Configure(IEnumerable<SortingLayerDefinition>? definitions)
    {
        var values = (definitions ?? CreateDefaults())
            .Select(static item => item with
            {
                Name = item.Name?.Trim() ?? string.Empty,
                BuiltInId = item.BuiltInId?.Trim() ?? string.Empty
            })
            .ToArray();
        if (values.Length is < SortingLayer.BuiltInLayerCount or > SortingLayer.MaximumIndex)
            throw new InvalidDataException(
                $"A project must define between {SortingLayer.BuiltInLayerCount} and " +
                $"{SortingLayer.MaximumIndex} layers.");
        for (var index = 0; index < values.Length; index++)
        {
            SortingLayer.Validate(values[index].Value);
            if (values[index].Value != (ulong)(index + SortingLayer.MinimumIndex))
                throw new InvalidDataException("Layer values must be contiguous one-based indices.");
        }
        AssignMissingBuiltInIds(values);
        var builtIns = values.Where(static item => item.IsBuiltIn)
            .Select(static item => item.BuiltInId).ToArray();
        if (builtIns.Length != SortingLayer.BuiltInLayerCount ||
            !BuiltInIds.All(id => builtIns.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("All five built-in Layer identities must be present exactly once.");
        if (values.Any(item => item.Name.Length == 0))
            throw new InvalidDataException("Layer names cannot be empty.");
        var duplicates = values.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
            throw new InvalidDataException($"Layer name '{duplicates.Key}' is not unique.");
        _layers = values;
        _view = Array.AsReadOnly(_layers);
        _version++;
    }

    internal static SortingLayerDefinition[] CreateDefaults() =>
    [
        new(1, "Default", true, false, DefaultId),
        new(2, "TransparentFX", true, false, TransparentFxId),
        new(3, "Ignore Raycast", true, false, IgnoreRaycastId),
        new(4, "Water", true, false, WaterId),
        new(5, "UI", true, true, UiId)
    ];

    private static void AssignMissingBuiltInIds(SortingLayerDefinition[] values)
    {
        var used = values.Where(static item => item.IsBuiltIn && !string.IsNullOrEmpty(item.BuiltInId))
            .Select(static item => item.BuiltInId).ToHashSet(StringComparer.Ordinal);
        var missing = new Queue<string>(BuiltInIds.Where(id => !used.Contains(id)));
        for (var index = 0; index < values.Length && missing.Count > 0; index++)
            if (values[index].IsBuiltIn && string.IsNullOrEmpty(values[index].BuiltInId))
                values[index] = values[index] with { BuiltInId = missing.Dequeue() };
    }
}
