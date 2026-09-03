namespace BEngine.Rendering.Rhi;

public static class GraphicsBackendCatalog
{
    private static readonly IReadOnlyList<GraphicsBackendDescriptor> Descriptors =
        Array.AsReadOnly<GraphicsBackendDescriptor>(
    [
        new(GraphicsBackend.OpenGL, "OpenGL", true, [GraphicsShaderLanguage.Glsl]),
        new(GraphicsBackend.Direct3D11, "Direct3D 11", false, [GraphicsShaderLanguage.Hlsl]),
        new(GraphicsBackend.Direct3D12, "Direct3D 12", false, [GraphicsShaderLanguage.Hlsl]),
        new(GraphicsBackend.Vulkan, "Vulkan", true,
            [GraphicsShaderLanguage.SpirV, GraphicsShaderLanguage.Glsl]),
        new(GraphicsBackend.WebGPU, "WebGPU", false, [GraphicsShaderLanguage.Wgsl]),
        new(GraphicsBackend.Metal, "Metal", false, [GraphicsShaderLanguage.Msl]),
        new(GraphicsBackend.OpenGLES, "OpenGL ES", false, [GraphicsShaderLanguage.Glsl]),
        new(GraphicsBackend.WebGL, "WebGL", false, [GraphicsShaderLanguage.Glsl])
    ]);

    public static IReadOnlyList<GraphicsBackendDescriptor> All => Descriptors;

    public static GraphicsBackendDescriptor Get(GraphicsBackend backend) =>
        backend == GraphicsBackend.Auto
            ? throw new ArgumentException("Auto is a selection policy, not a graphics backend.", nameof(backend))
            : Descriptors.First(item => item.Backend == backend);
}
