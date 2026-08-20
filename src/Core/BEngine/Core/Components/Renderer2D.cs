namespace BEngine;

public abstract class Renderer2D : MonoBehaviour
{
    private ulong _sortingLayer = SortingLayer.Default;
    private int _orderInLayer;
    private Fix64 _opacity = Fix64.One;

    public ulong sortingLayer
    {
        get { MainThreadGuard.Ensure(); return _sortingLayer; }
        set
        {
            MainThreadGuard.Ensure();
            SortingLayer.Validate(value);
            if (!supportsUiSortingLayers && SortingLayer.IsUi(value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    "This renderer only supports world sorting layers.");
            _sortingLayer = value;
        }
    }

    public int orderInLayer
    {
        get { MainThreadGuard.Ensure(); return _orderInLayer; }
        set { MainThreadGuard.Ensure(); _orderInLayer = value; }
    }

    [Range(0, 1)]
    public Fix64 opacity
    {
        get { MainThreadGuard.Ensure(); return _opacity; }
        set { MainThreadGuard.Ensure(); _opacity = Fix64.Clamp(value, Fix64.Zero, Fix64.One); }
    }

    internal RenderTransparency TransparencyUnchecked(Color color) =>
        _opacity < Fix64.One || color.a < Fix64.One
            ? RenderTransparency.Transparent
            : RenderTransparency.Opaque;

    protected virtual bool supportsUiSortingLayers => false;
}
