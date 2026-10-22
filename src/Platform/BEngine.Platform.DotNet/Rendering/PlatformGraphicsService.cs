using BEngine.Rendering.Rhi;

namespace BEngine.Platform;

public sealed class PlatformGraphicsService : IGraphicsPlatformService
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


    public GraphicsBackend DefaultBackend => GraphicsBackend.Vulkan;
    public GraphicsBackend PreferredBackend { get; set; } = GraphicsBackend.Auto;
    public GraphicsDeviceCapabilities? ActiveCapabilities { get; private set; }
    public IReadOnlyList<GraphicsBackendDescriptor> Backends => Descriptors;

    public void ReportActive(GraphicsDeviceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ActiveCapabilities = capabilities;
    }

    public GraphicsShaderPair ResolveShaderVariant(GraphicsDeviceCapabilities capabilities, string resourceStem)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceStem);
        if (capabilities.Backend == GraphicsBackend.Vulkan) return Pair("vulkan", "glsl", GraphicsShaderLanguage.Glsl);
        if (capabilities.Supports(GraphicsShaderLanguage.Glsl)) return Pair("opengl", "glsl", GraphicsShaderLanguage.Glsl);
        if (capabilities.Supports(GraphicsShaderLanguage.Hlsl)) return Pair("direct3d", "hlsl", GraphicsShaderLanguage.Hlsl);
        if (capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var source = DefaultShaderResources.Load(resourceStem + ".webgpu.wgsl");
            return new(new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, source, "vs_main"),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, source, "fs_main"));
        }
        throw new NotSupportedException($"No shader variant for {capabilities.Backend}: {resourceStem}.");

        GraphicsShaderPair Pair(string backend, string extension, GraphicsShaderLanguage language) => new(
            new(GraphicsShaderStage.Vertex, language, DefaultShaderResources.Load($"{resourceStem}.{backend}.vert.{extension}")),
            new(GraphicsShaderStage.Fragment, language, DefaultShaderResources.Load($"{resourceStem}.{backend}.frag.{extension}")));
    }

    public GraphicsRect ResolveViewport(
        GraphicsRect target, Rect normalizedViewport, GraphicsBackend backend)
    {
        target.Validate();
        var x = Math.Clamp((float)normalizedViewport.x, 0f, 1f);
        var y = Math.Clamp((float)normalizedViewport.y, 0f, 1f);
        var right = Math.Clamp((float)normalizedViewport.xMax, x, 1f);
        var top = Math.Clamp((float)normalizedViewport.yMax, y, 1f);
        var leftPixel = Math.Clamp((int)MathF.Floor(x * target.Width), 0, target.Width);
        var rightPixel = Math.Clamp((int)MathF.Ceiling(right * target.Width), leftPixel, target.Width);
        int yPixel;
        int bottomPixel;
        if (backend is GraphicsBackend.OpenGL or GraphicsBackend.OpenGLES or GraphicsBackend.WebGL)
        {
            yPixel = Math.Clamp((int)MathF.Floor(y * target.Height), 0, target.Height);
            bottomPixel = Math.Clamp((int)MathF.Ceiling(top * target.Height), yPixel, target.Height);
        }
        else
        {
            yPixel = Math.Clamp((int)MathF.Floor((1f - top) * target.Height), 0, target.Height);
            bottomPixel = Math.Clamp((int)MathF.Ceiling((1f - y) * target.Height), yPixel, target.Height);
        }
        return new GraphicsRect(target.X + leftPixel, target.Y + yPixel,
            rightPixel - leftPixel, bottomPixel - yPixel);
    }

}
