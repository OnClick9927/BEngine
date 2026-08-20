namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsRasterizerState(bool DepthBiasEnabled, float SlopeScale, float ConstantBias)
{
    public static GraphicsRasterizerState Default => new(false, 0, 0);
    public static GraphicsRasterizerState WithDepthBias(float slopeScale, float constantBias) =>
        new(true, slopeScale, constantBias);
}
