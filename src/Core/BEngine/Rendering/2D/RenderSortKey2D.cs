namespace BEngine;

public readonly record struct RenderSortKey2D : IComparable<RenderSortKey2D>
{
    public RenderSortKey2D(
        ulong layer,
        int orderInLayer,
        long hierarchyOrder,
        RenderTransparency transparency,
        long submissionOrder,
        int groupLayer = 0,
        long? groupOrder = null,
        long groupHierarchyOrder = 0)
    {
        SortingLayer.Validate(layer);
        Layer = layer;
        OrderInLayer = orderInLayer;
        HierarchyOrder = hierarchyOrder;
        Transparency = transparency;
        SubmissionOrder = submissionOrder;
        GroupLayer = groupLayer;
        GroupOrder = groupOrder ?? orderInLayer;
        GroupHierarchyOrder = groupHierarchyOrder;
    }

    public ulong Layer { get; }
    public int OrderInLayer { get; }
    public long HierarchyOrder { get; }
    public RenderTransparency Transparency { get; }
    public long SubmissionOrder { get; }
    public int GroupLayer { get; }
    public long GroupOrder { get; }
    public long GroupHierarchyOrder { get; }

    public int CompareTo(RenderSortKey2D other)
    {
        var result = Layer.CompareTo(other.Layer);
        if (result != 0) return result;
        result = GroupLayer.CompareTo(other.GroupLayer);
        if (result != 0) return result;
        result = GroupOrder.CompareTo(other.GroupOrder);
        if (result != 0) return result;
        result = GroupHierarchyOrder.CompareTo(other.GroupHierarchyOrder);
        if (result != 0) return result;
        result = OrderInLayer.CompareTo(other.OrderInLayer);
        if (result != 0) return result;
        result = HierarchyOrder.CompareTo(other.HierarchyOrder);
        if (result != 0) return result;
        result = ((byte)Transparency).CompareTo((byte)other.Transparency);
        return result != 0 ? result : SubmissionOrder.CompareTo(other.SubmissionOrder);
    }
}
