using BEngine.Rendering;

namespace BEngine;

internal sealed record PixelPerfectPolicy(int AssetsPPU, int ReferenceWidth, int ReferenceHeight, bool PixelSnapping)
    : IStableRenderCameraPolicy
{
    public bool AlignViewportCenter => PixelSnapping;
    public RenderCamera ForViewport(RenderCamera camera, int width, int height)
    {
        var ratio = Math.Max(1, Math.Min(Math.Max(1, width) / ReferenceWidth, Math.Max(1, height) / ReferenceHeight));
        return camera with { Size = (Fix64)Math.Max(1, height) / (2 * AssetsPPU * ratio), Position = SnapWorldPoint(camera.Position) };
    }
    public Vector2 SnapWorldPoint(Vector2 point) => PixelSnapping
        ? new Vector2(Mathf.Round(point.x * AssetsPPU) / AssetsPPU, Mathf.Round(point.y * AssetsPPU) / AssetsPPU)
        : point;
}
