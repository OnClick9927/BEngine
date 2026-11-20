namespace BEngine;

public readonly struct LayerMask : IEquatable<LayerMask>
{
    public ulong value { get; }

    public LayerMask(ulong value) => this.value = value;

    public static implicit operator ulong(LayerMask mask) => mask.value;
    public static implicit operator LayerMask(ulong value) => new(value);
    public static LayerMask operator |(LayerMask left, LayerMask right) => new(left.value | right.value);
    public static LayerMask operator &(LayerMask left, LayerMask right) => new(left.value & right.value);
    public static LayerMask operator ~(LayerMask value) => new(~value.value);

    public static IReadOnlyList<LayerDefinition> layers => LayerRegistry.layers;
    public static int version => LayerRegistry.version;

    public static ulong NameToLayer(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName)) return 0;
        var layer = Matches906();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.LayerDefinition Matches906()
        {
            var matchSource906 = LayerRegistry.layers;
            ArgumentNullException.ThrowIfNull(matchSource906, "source");
            global::System.ReadOnlySpan<global::BEngine.LayerDefinition> matchValues906;
            if (matchSource906.GetType() == typeof(global::BEngine.LayerDefinition[]))
                matchValues906 = (global::BEngine.LayerDefinition[])(object)matchSource906;
            else if (matchSource906.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.LayerDefinition>))
                matchValues906 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.LayerDefinition>)(object)matchSource906);
            else
            {
                foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.LayerDefinition>)matchSource906)
                {
                    if (item.Name.Equals(layerName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return item;
                }
        
                return default!;
            }
        
            foreach (var item in matchValues906)
            {
                if (item.Name.Equals(layerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        
            return default!;
        }
        return layer.Value;
    }

    public static string LayerToName(ulong layer) =>
        SortingLayer.IsValid(layer) ? LayerRegistry.NameOf(layer) : string.Empty;

    public static LayerMask GetMask(params string[] layerNames) => new(layerNames.Aggregate(0UL,
        static (mask, name) => mask | MaskForLayer(NameToLayer(name))));

    public static ulong MaskForLayer(ulong layer) => layer == 0 ? 0 : SortingLayer.ToMask(layer);

    public bool Contains(ulong layer)
    {
        SortingLayer.Validate(layer);
        return (value & SortingLayer.ToMask(layer)) != 0;
    }

    public bool Equals(LayerMask other) => value == other.value;
    public override bool Equals(object? obj) => obj is LayerMask other && Equals(other);
    public override int GetHashCode() => value.GetHashCode();
}
