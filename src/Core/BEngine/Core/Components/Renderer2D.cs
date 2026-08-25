namespace BEngine;

public abstract class Renderer2D : MonoBehaviour
{
    public ulong sortingLayer
    {
        get;
        set
        {
            SortingLayer.Validate(value);
            if (!supportsUiSortingLayers && SortingLayer.IsUi(value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    "This renderer only supports world sorting layers.");
            field = value;
        }
    } = SortingLayer.Default;

    public int orderInLayer { get; set; }

    [Range(0, 1)]
    public Fix64 opacity
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
    } = Fix64.One;

    internal RenderTransparency TransparencyUnchecked(Color color) =>
        opacity < Fix64.One || color.a < Fix64.One
            ? RenderTransparency.Transparent
            : RenderTransparency.Opaque;

    protected virtual bool supportsUiSortingLayers => false;
}
