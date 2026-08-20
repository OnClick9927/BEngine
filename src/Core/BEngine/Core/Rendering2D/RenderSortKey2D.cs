namespace BEngine;

public readonly record struct RenderSortKey2D : IComparable<RenderSortKey2D>
{
    public RenderSortKey2D(
        ulong layer,
        int orderInLayer,
        long hierarchyOrder,
        RenderTransparency transparency,
        long submissionOrder)
    {
        SortingLayer.Validate(layer);
        Layer = layer;
        OrderInLayer = orderInLayer;
        HierarchyOrder = hierarchyOrder;
        Transparency = transparency;
        SubmissionOrder = submissionOrder;
    }

    public ulong Layer { get; }
    public int OrderInLayer { get; }
    public long HierarchyOrder { get; }
    public RenderTransparency Transparency { get; }
    public long SubmissionOrder { get; }

    public int CompareTo(RenderSortKey2D other)
    {
        var result = Layer.CompareTo(other.Layer);
        if (result != 0) return result;
        result = OrderInLayer.CompareTo(other.OrderInLayer);
        if (result != 0) return result;
        result = HierarchyOrder.CompareTo(other.HierarchyOrder);
        if (result != 0) return result;
        result = Transparency.CompareTo(other.Transparency);
        return result != 0 ? result : SubmissionOrder.CompareTo(other.SubmissionOrder);
    }
}
