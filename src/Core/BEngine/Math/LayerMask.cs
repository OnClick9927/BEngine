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

    public static IReadOnlyList<SortingLayerDefinition> layers => SortingLayerRegistry.layers;
    public static int version => SortingLayerRegistry.version;

    public static ulong NameToLayer(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName)) return 0;
        var layer = SortingLayerRegistry.layers.FirstOrDefault(item =>
            item.Name.Equals(layerName.Trim(), StringComparison.OrdinalIgnoreCase));
        return layer.Value;
    }

    public static string LayerToName(ulong layer) =>
        SortingLayer.IsValid(layer) ? SortingLayerRegistry.NameOf(layer) : string.Empty;

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
