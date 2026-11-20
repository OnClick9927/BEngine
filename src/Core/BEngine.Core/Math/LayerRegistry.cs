namespace BEngine;

public static class LayerRegistry
{
    internal const string DefaultId = "Default";
    internal const string TransparentFxId = "TransparentFX";
    internal const string IgnoreRaycastId = "IgnoreRaycast";
    internal const string WaterId = "Water";
    internal const string UiId = "UI";
    private static readonly string[] BuiltInIds =
        [DefaultId, TransparentFxId, IgnoreRaycastId, WaterId, UiId];
    private static LayerDefinition[] _layers = CreateDefaults();
    private static IReadOnlyList<LayerDefinition> _view = Array.AsReadOnly(_layers);
    private static Dictionary<string, ulong> _builtInValues = BuildIdentityLookup(_layers);
    private static int _version;

    public static int version => _version;
    public static IReadOnlyList<LayerDefinition> layers => _view;

    // Configure guarantees contiguous one-based indices. Direct indexing avoids
    // capturing LINQ predicates in rendering, layer masks, and editor controls.
    public static bool IsDefined(ulong value) => value >= SortingLayer.MinimumIndex && value <= (ulong)_layers.Length;

    public static bool IsUi(ulong value) => IsDefined(value) && _layers[(int)value - SortingLayer.MinimumIndex].IsUi;

    internal static ulong ValueOfBuiltIn(string id)
    {
        return _builtInValues.TryGetValue(id, out var value)
            ? value
            : throw new InvalidOperationException($"The built-in Layer '{id}' is not configured.");
    }

    public static string NameOf(ulong value)
    {
        SortingLayer.Validate(value);
        return IsDefined(value) ? _layers[(int)value - SortingLayer.MinimumIndex].Name : string.Empty;
    }

    public static void Configure(IEnumerable<LayerDefinition>? definitions)
    {
        var values = Gather1806((definitions ?? CreateDefaults()));
        [global::BEngine.SkipLoopGuard]
        global::BEngine.LayerDefinition[] Gather1806(global::System.Collections.Generic.IEnumerable<global::BEngine.LayerDefinition> items1806)
        {
            ArgumentNullException.ThrowIfNull(items1806, "source");
            global::BEngine.LayerDefinition Evaluate1806(global::BEngine.LayerDefinition item) => item with
            {
                Name = item.Name?.Trim() ?? string.Empty,
                BuiltInId = item.BuiltInId?.Trim() ?? string.Empty
            };
            global::System.ReadOnlySpan<global::BEngine.LayerDefinition> values;
            if (items1806.GetType() == typeof(global::BEngine.LayerDefinition[]))
                values = (global::BEngine.LayerDefinition[])items1806;
            else if (items1806.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.LayerDefinition>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.LayerDefinition>)items1806);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items1806, Evaluate1806));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<global::BEngine.LayerDefinition>();
            var result = new global::BEngine.LayerDefinition[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate1806(values[index]);
            return result;
        }
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
        bool Query2937()
        {
            var querySource2937 = BuiltInIds;
            ArgumentNullException.ThrowIfNull(querySource2937, "source");
            if (querySource2937.GetType() == typeof(string[]) || querySource2937.GetType() == typeof(global::System.Collections.Generic.List<string>))
            {
                global::System.ReadOnlySpan<string> queryValues2937 = querySource2937.GetType() == typeof(string[]) ? (string[])(object)querySource2937 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource2937);
                foreach (var id in queryValues2937)
                {
                    if (!(builtIns.Contains(id, StringComparer.Ordinal)))
                        return false;
                }
            }
            else
            {
                foreach (var id in querySource2937)
                {
                    if (!(builtIns.Contains(id, StringComparer.Ordinal)))
                        return false;
                }
            }
        
            return true;
        }
        
        if (builtIns.Length != SortingLayer.BuiltInLayerCount || !Query2937())
            throw new InvalidDataException("All five built-in Layer identities must be present exactly once.");
        bool __queryResult3132;
        {
            var __querySource3132 = values;
            ArgumentNullException.ThrowIfNull(__querySource3132, "source");
            __queryResult3132 = false;
            foreach (var __queryItem3132 in __querySource3132)
            {
                if ((__queryItem3132.Name.Length == 0))
                {
                    __queryResult3132 = true;
                    break;
                }
            }
        }
        
        if (__queryResult3132)
            throw new InvalidDataException("Layer names cannot be empty.");
        var duplicates = Query4785(values.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
        [global::BEngine.SkipLoopGuard]
        global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>? Query4785(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>> querySource4785)
        {
            ArgumentNullException.ThrowIfNull(querySource4785, "source");
            bool Matches4785(global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition> group) => group.Count() > 1;
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>> queryValues4785;
            if (querySource4785.GetType() == typeof(global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>[]))
                queryValues4785 = (global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>[])querySource4785;
            else if (querySource4785.GetType() == typeof(global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>>))
                queryValues4785 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.LayerDefinition>>)querySource4785);
            else
            {
                foreach (var group in querySource4785)
                    if (Matches4785(group))
                        return group;
                return default!;
            }
        
            foreach (var group in queryValues4785)
                if (Matches4785(group))
                    return group;
            return default!;
        }
        if (duplicates is not null)
            throw new InvalidDataException($"Layer name '{duplicates.Key}' is not unique.");
        var builtInValues = BuildIdentityLookup(values);
        _layers = values;
        _view = Array.AsReadOnly(_layers);
        _builtInValues = builtInValues;
        _version++;
    }

    private static Dictionary<string, ulong> BuildIdentityLookup(LayerDefinition[] values)
    {
        var result = new Dictionary<string, ulong>(BuiltInIds.Length, StringComparer.Ordinal);
        foreach (var layer in values)
            if (layer.BuiltInId.Length > 0) result.TryAdd(layer.BuiltInId, layer.Value);
        return result;
    }

    public static LayerDefinition[] CreateDefaults() =>
    [
        new(1, "Default", true, false, DefaultId),
        new(2, "TransparentFX", true, false, TransparentFxId),
        new(3, "Ignore Raycast", true, false, IgnoreRaycastId),
        new(4, "Water", true, false, WaterId),
        new(5, "UI", true, true, UiId)
    ];

    private static void AssignMissingBuiltInIds(LayerDefinition[] values)
    {
        var used = values.Where(static item => item.IsBuiltIn && !string.IsNullOrEmpty(item.BuiltInId))
            .Select(static item => item.BuiltInId).ToHashSet(StringComparer.Ordinal);
        var missing = new Queue<string>(BuiltInIds.Where(id => !used.Contains(id)));
        for (var index = 0; index < values.Length && missing.Count > 0; index++)
            if (values[index].IsBuiltIn && string.IsNullOrEmpty(values[index].BuiltInId))
                values[index] = values[index] with { BuiltInId = missing.Dequeue() };
    }
}
