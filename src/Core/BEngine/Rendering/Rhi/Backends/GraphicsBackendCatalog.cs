namespace BEngine.Rendering.Rhi;

public static class GraphicsBackendCatalog
{
    public static IReadOnlyList<GraphicsBackendDescriptor> All => GraphicsUtility.Current.Backends;
    public static GraphicsBackendDescriptor Get(GraphicsBackend backend) =>
        backend == GraphicsBackend.Auto
            ? throw new ArgumentException("Auto is a selection policy, not a graphics backend.", nameof(backend))
            : All.First(item => item.Backend == backend);
}
