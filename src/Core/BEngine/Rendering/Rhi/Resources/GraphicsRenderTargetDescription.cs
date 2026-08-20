namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsRenderTargetDescription(
    int Width,
    int Height,
    GraphicsTextureFormat? ColorFormat,
    GraphicsTextureFormat? DepthFormat,
    bool SampleColor = false,
    bool SampleDepth = false,
    GraphicsTextureFilter Filter = GraphicsTextureFilter.Nearest)
{
    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (ColorFormat is null && DepthFormat is null)
            throw new ArgumentException("A render target requires a color or depth attachment.");
        if (ColorFormat is GraphicsTextureFormat.Depth24Unorm or GraphicsTextureFormat.Depth24Stencil8)
            throw new ArgumentException("The color attachment must use a color format.", nameof(ColorFormat));
        if (DepthFormat is not null and not GraphicsTextureFormat.Depth24Unorm and not GraphicsTextureFormat.Depth24Stencil8)
            throw new ArgumentException("The depth attachment must use a depth format.", nameof(DepthFormat));
    }
}
