namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsColorReadbackCapabilities(
    bool IsAvailable,
    int MaximumBytes,
    string UnavailableReason = "")
{
    public static GraphicsColorReadbackCapabilities Unavailable(string reason) =>
        new(false, 0, string.IsNullOrWhiteSpace(reason) ? "Color readback is unavailable." : reason);
}
