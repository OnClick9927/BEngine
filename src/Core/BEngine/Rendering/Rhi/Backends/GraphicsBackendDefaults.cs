namespace BEngine.Rendering.Rhi;

public static class GraphicsBackendDefaults
{
    public const GraphicsBackend Default = GraphicsBackend.Vulkan;

    public static GraphicsBackend Parse(string? value) =>
        Enum.TryParse<GraphicsBackend>(value, true, out var backend) ? backend : Default;
}
