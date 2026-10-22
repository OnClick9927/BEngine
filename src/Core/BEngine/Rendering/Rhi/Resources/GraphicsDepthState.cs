namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsDepthState(bool TestEnabled, bool WriteEnabled)
{
    public static GraphicsDepthState Disabled => new(false, false);
    public static GraphicsDepthState Default => new(true, true);
}
