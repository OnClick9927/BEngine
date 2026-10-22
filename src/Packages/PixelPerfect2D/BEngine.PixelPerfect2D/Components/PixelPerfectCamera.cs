namespace BEngine;

[AddComponentMenu("Rendering/Pixel Perfect Camera")]
[RequireComponent(typeof(Camera2D))]
[DisallowMultipleComponent]
public sealed class PixelPerfectCamera : Behaviour, Rendering.IRenderCameraPolicyProvider
{
    private PixelPerfectPolicy? _cachedPolicy;

    public Rendering.IRenderCameraPolicy CreatePolicy()
    {
        // RenderCamera retains an immutable policy snapshot. Reuse that snapshot
        // until one of its inputs changes; moving the camera is not an input.
        var policy = _cachedPolicy;
        if (policy is null || policy.AssetsPPU != assetsPPU ||
            policy.ReferenceWidth != refResolutionX || policy.ReferenceHeight != refResolutionY ||
            policy.PixelSnapping != pixelSnapping)
            _cachedPolicy = policy = new PixelPerfectPolicy(assetsPPU, refResolutionX, refResolutionY, pixelSnapping);
        return policy;
    }

    public int assetsPPU { get; set => field = Math.Clamp(value, 1, 10000); } = 100;
    public int refResolutionX { get; set => field = Math.Max(1, value); } = 320;
    public int refResolutionY { get; set => field = Math.Max(1, value); } = 180;
    public bool pixelSnapping { get; set; } = true;

    public int GetPixelRatio(int width, int height) =>
        Math.Max(1, Math.Min(Math.Max(1, width) / refResolutionX, Math.Max(1, height) / refResolutionY));

    public Vector2 RoundToPixel(Vector2 position) => new(
        Mathf.Round(position.x * assetsPPU) / assetsPPU,
        Mathf.Round(position.y * assetsPPU) / assetsPPU);
}
