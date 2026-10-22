namespace BEngine.Rendering.Rhi;

[Flags]
public enum GraphicsDeviceFeatures
{
    None = 0,
    Rasterization = 1 << 0,
    ShaderPrograms = 1 << 1,
    StaticVertexBuffers = 1 << 2,
    DynamicVertexBuffers = 1 << 3,
    SampledTextures = 1 << 4,
    RenderTargets = 1 << 5,
    DepthTextures = 1 << 6,
    AlphaBlending = 1 << 7,
    ScissorRectangles = 1 << 8,
    DepthBias = 1 << 9
}
