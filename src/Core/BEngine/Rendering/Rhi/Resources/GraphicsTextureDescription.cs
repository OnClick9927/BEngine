namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsTextureDescription(
    int Width,
    int Height,
    GraphicsTextureFormat Format,
    GraphicsTextureUsage Usage,
    GraphicsTextureFilter MinFilter = GraphicsTextureFilter.Linear,
    GraphicsTextureFilter MagFilter = GraphicsTextureFilter.Linear,
    GraphicsTextureAddressMode AddressMode = GraphicsTextureAddressMode.ClampToEdge)
{
    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (Usage == GraphicsTextureUsage.None) throw new ArgumentOutOfRangeException(nameof(Usage));
        if (!Enum.IsDefined(MinFilter)) throw new ArgumentOutOfRangeException(nameof(MinFilter));
        if (!Enum.IsDefined(MagFilter)) throw new ArgumentOutOfRangeException(nameof(MagFilter));
        if (!Enum.IsDefined(AddressMode)) throw new ArgumentOutOfRangeException(nameof(AddressMode));
    }
}
