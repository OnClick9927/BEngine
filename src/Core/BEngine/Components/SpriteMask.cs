namespace BEngine;

[AddComponentMenu("Rendering/Sprite Mask")]
public sealed class SpriteMask : Renderer2D
{
    public Sprite? sprite { get; set; }
    public Vector2 size { get; set; } = Vector2.one;
    public Vector2 pivot { get; set; } = new(Fix64.Half, Fix64.Half);
    [Range(0, 1)] public Fix64 alphaCutoff { get; set; }
    public bool isCustomRangeActive { get; set; }
    public ulong backSortingLayer
    {
        get;
        set
        {
            if (!SortingLayer.IsValid(value)) throw new ArgumentOutOfRangeException(nameof(value));
            field = value;
        }
    } = SortingLayer.Default;
    public int backSortingOrder { get; set; }
    public ulong frontSortingLayer
    {
        get;
        set
        {
            if (!SortingLayer.IsValid(value)) throw new ArgumentOutOfRangeException(nameof(value));
            field = value;
        }
    } = SortingLayer.Default;
    public int frontSortingOrder { get; set; }

    internal bool AppliesTo(Renderer2D renderer)
    {
        if (!isCustomRangeActive) return true;
        return Compare(renderer.sortingLayer, renderer.orderInLayer,
                   backSortingLayer, backSortingOrder) >= 0 &&
               Compare(renderer.sortingLayer, renderer.orderInLayer,
                   frontSortingLayer, frontSortingOrder) <= 0;
    }

    internal bool Contains(Vector2 worldPoint)
    {
        var local = transform.InverseTransformPoint(worldPoint);
        var minimum = new Vector2(-pivot.x * size.x, -pivot.y * size.y);
        var maximum = minimum + size;
        var minX = Fix64.Min(minimum.x, maximum.x);
        var maxX = Fix64.Max(minimum.x, maximum.x);
        var minY = Fix64.Min(minimum.y, maximum.y);
        var maxY = Fix64.Max(minimum.y, maximum.y);
        return local.x >= minX && local.x <= maxX && local.y >= minY && local.y <= maxY;
    }

    private static int Compare(ulong layer, int order, ulong otherLayer, int otherOrder)
    {
        var layerComparison = layer.CompareTo(otherLayer);
        return layerComparison != 0 ? layerComparison : order.CompareTo(otherOrder);
    }
}
