namespace BEngine.Editor.Rendering;

internal readonly record struct GpuCanvasAtlasRegion(float U0, float V0, float U1, float V1)
{
    public static GpuCanvasAtlasRegion Full { get; } = new(0, 0, 1, 1);
}
