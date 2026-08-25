namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsRasterizerState(
    bool DepthBiasEnabled,
    float SlopeScale,
    float ConstantBias,
    GraphicsCullMode CullMode)
{
    public static GraphicsRasterizerState Default => new(false, 0, 0, GraphicsCullMode.None);
    public static GraphicsRasterizerState CullBackFaces => new(false, 0, 0, GraphicsCullMode.Back);
    public static GraphicsRasterizerState WithDepthBias(
        float slopeScale, float constantBias, GraphicsCullMode cullMode = GraphicsCullMode.None) =>
        new(true, slopeScale, constantBias, cullMode);
}
