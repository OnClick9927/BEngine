namespace BEngine.Rendering.Rhi;

[Flags]
public enum GraphicsClearFlags
{
    None = 0,
    Color = 1 << 0,
    Depth = 1 << 1,
    Stencil = 1 << 2
}
