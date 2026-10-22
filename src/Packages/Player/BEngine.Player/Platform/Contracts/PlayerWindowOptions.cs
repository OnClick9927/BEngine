using BEngine.Rendering.Rhi;

namespace BEngine.Player;

public sealed record PlayerWindowOptions(
    string Title,
    int Width,
    int Height,
    bool VSync)
{
    public IReadOnlyList<GraphicsBackend> GraphicsBackends { get; init; } = [GraphicsBackend.Auto];
}
