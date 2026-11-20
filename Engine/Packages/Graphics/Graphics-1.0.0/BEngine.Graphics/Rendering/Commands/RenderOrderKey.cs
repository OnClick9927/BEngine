namespace BEngine;

[RuntimeTypeAlias("BEngine.RenderSortKey2D")]
[RuntimeTypeAlias("BEngine.RenderSortKey2D, BEngine.Graphics")]
[RuntimeTypeAlias("BEngine.RenderSortKey2D, BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public readonly record struct RenderOrderKey : IComparable<RenderOrderKey>
{
    public RenderOrderKey(
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

    public int CompareTo(RenderOrderKey other)
    {
        return Compare(in this, in other);
    }

    // Array-backed Web sorting can compare keys without copying the 64-byte
    // right operand. Keep the public value-parameter path unchanged elsewhere.
    internal static int Compare(in RenderOrderKey left, in RenderOrderKey other)
    {
        var result = left.Layer.CompareTo(other.Layer);
        if (result != 0) return result;
        result = left.GroupLayer.CompareTo(other.GroupLayer);
        if (result != 0) return result;
        result = left.GroupOrder.CompareTo(other.GroupOrder);
        if (result != 0) return result;
        result = left.GroupHierarchyOrder.CompareTo(other.GroupHierarchyOrder);
        if (result != 0) return result;
        result = left.OrderInLayer.CompareTo(other.OrderInLayer);
        if (result != 0) return result;
        result = left.HierarchyOrder.CompareTo(other.HierarchyOrder);
        if (result != 0) return result;
        result = ((byte)left.Transparency).CompareTo((byte)other.Transparency);
        return result != 0 ? result : left.SubmissionOrder.CompareTo(other.SubmissionOrder);
    }
}
