namespace BEngine.Rendering.Rhi;

[Flags]
public enum GraphicsTextureUsage
{
    None = 0,
    Sampled = 1 << 0,
    RenderTarget = 1 << 1
}
