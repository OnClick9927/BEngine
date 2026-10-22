using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.PostProcessing;

internal sealed class PostProcessRenderer2D : IScenePostProcessor2D
{
    private const int FloatsPerVertex = 28;
    private static readonly GraphicsVertexLayout Layout = new(
        FloatsPerVertex * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 2, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 4, 4 * sizeof(float)),
            new GraphicsVertexAttribute(3, 4, 8 * sizeof(float)),
            new GraphicsVertexAttribute(4, 4, 12 * sizeof(float)),
            new GraphicsVertexAttribute(5, 4, 16 * sizeof(float)),
            new GraphicsVertexAttribute(6, 4, 20 * sizeof(float)),
            new GraphicsVertexAttribute(7, 4, 24 * sizeof(float))
        ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram[] _programs = new IGraphicsProgram[8];
    private readonly IGraphicsMesh _mesh;
    private readonly GraphicsMeshUploadCache _uploads;
    private readonly List<float> _vertices = new(6 * FloatsPerVertex);
    private bool _pipelinesWarmed;
    private bool _disposed;

    public string packageId => "com.bengine.postprocessing";

    public PostProcessRenderer2D(IGraphicsDevice device)
    {
        _device = device;
        device.Capabilities.Require(GraphicsDeviceFeatures.Rasterization |
                                    GraphicsDeviceFeatures.ShaderPrograms |
                                    GraphicsDeviceFeatures.DynamicVertexBuffers |
                                    GraphicsDeviceFeatures.SampledTextures);
        var (vertex, fragment) = ResolveShaders(device);
        IGraphicsMesh? mesh = null;
        try
        {
            for (var activity = 0; activity < _programs.Length; activity++)
                _programs[activity] = device.CreateProgram(new GraphicsShaderProgramDescription(
                    "BEngine.PostProcessing.Texture." + activity, vertex, Specialize(fragment, activity)));
            mesh = device.CreateMesh(new GraphicsMeshDescription(
                "BEngine.PostProcessing.Fullscreen", ReadOnlyMemory<float>.Empty, Layout,
                GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
            _mesh = mesh;
            _uploads = new(_mesh);
        }
        catch
        {
            mesh?.Dispose();
            foreach (var program in _programs) program?.Dispose();
            throw;
        }
    }

    public bool IsActive(Scene scene, Camera2D camera)
    {
        var layer = camera.GetComponent<PostProcessLayer>();
        if (layer is not { enabled: true } || !layer.gameObject.activeInHierarchy) return false;
        // Profiles, volume weights and triggers are mutable even within one
        // frame. An inactive probe may never be followed by Render, so neither
        // its answer nor its resolved parameters can be retained here.
        return PostProcessStack2D.Resolve(scene, layer).IsActive;
    }

    public void Render(Scene scene, Camera2D camera, IGraphicsTexture2D source, GraphicsRect destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Width < 0 || destination.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(destination), "Viewport dimensions cannot be negative.");
        if (destination.Width <= 0 || destination.Height <= 0) return;
        using var gpuPass = GraphicsFrameTimingService.IsPassTimingEnabled
            ? (_device as IGraphicsPassTimingSource)?.BeginGpuPass("PostProcess.Composite") : null;
        var layer = camera.GetComponent<PostProcessLayer>() ??
                    throw new InvalidOperationException("The camera has no PostProcessLayer.");
        var stack = PostProcessStack2D.Resolve(scene, layer);
        BuildVertices(destination.Width, destination.Height, source.Description.Width,
            source.Description.Height, stack, layer.stopNaNPropagation);
        _uploads.Update(CollectionsMarshal.AsSpan(_vertices));
        _device.SetViewport(destination);
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.Disabled);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        _device.BindTexture(0, source);
        try
        {
            if (!_pipelinesWarmed)
            {
                // Program creation alone does not create a native graphics pipeline.
                // Draw on the real output attachment, with the real source binding,
                // so its format/depth/sample state matches the ensuing composite.
                // Every warmup pixel is inside this destination and is overwritten
                // below: the final quad covers the entire viewport, with blending
                // and depth disabled and no fragment discard, including alpha=0.
                _device.SetScissor(new GraphicsRect(0, 0, 1, 1));
                foreach (var program in _programs) Draw(program, destination);
            }
            _device.SetScissor(new GraphicsRect(0, 0, destination.Width, destination.Height));
            var activity = (stack.BloomIntensity > Fix64.Zero ? 1 : 0) |
                           (stack.ChromaticAberration > Fix64.Zero ? 2 : 0) |
                           (stack.FilmGrain > Fix64.Zero ? 4 : 0);
            Draw(_programs[activity], destination);
            _pipelinesWarmed = true;
        }
        finally { _device.SetScissor(null); }
    }

    private void Draw(IGraphicsProgram program, GraphicsRect destination)
    {
        program.Bind();
        program.SetFloat("uViewportWidth", destination.Width);
        program.SetFloat("uViewportHeight", destination.Height);
        program.SetInt("uTexture", 0);
        _device.Draw(_mesh);
    }

    private static GraphicsShaderSource Specialize(GraphicsShaderSource source, int activity)
    {
        var code = source.Code;
        code = ReplaceActivity(code, "BLOOM", (activity & 1) != 0);
        code = ReplaceActivity(code, "CHROMA", (activity & 2) != 0);
        code = ReplaceActivity(code, "GRAIN", (activity & 4) != 0);
        return source with { Code = code };
    }

    private static string ReplaceActivity(string code, string feature, bool enabled)
    {
        var begin = "/* BENGINE_POST_" + feature + " */";
        const string end = "/* BENGINE_POST_ACTIVITY_END */";
        var start = code.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing postprocess activity marker: " + feature);
        var finish = code.IndexOf(end, start + begin.Length, StringComparison.Ordinal);
        if (finish < 0) throw new InvalidOperationException("Unclosed postprocess activity marker: " + feature);
        return string.Concat(code.AsSpan(0, start), enabled ? "true" : "false", code.AsSpan(finish + end.Length));
    }

    private void BuildVertices(int width, int height, int sourceWidth, int sourceHeight,
        PostProcessStack2D stack, bool stopNaNPropagation)
    {
        _vertices.Clear();
        ReadOnlySpan<float> color = stackalloc float[]
        {
            (float)stack.Exposure, (float)stack.Contrast / 100f,
            (float)stack.Saturation / 100f, (float)stack.HueShift
        };
        ReadOnlySpan<float> filter = stackalloc float[]
        {
            (float)stack.ColorFilter.r, (float)stack.ColorFilter.g,
            (float)stack.ColorFilter.b, (float)stack.ColorFilter.a
        };
        ReadOnlySpan<float> vignette = stackalloc float[]
        {
            (float)stack.VignetteIntensity,
            Math.Max(0.001f, (float)stack.VignetteSmoothness),
            Math.Max(0.001f, (float)stack.VignetteRoundness),
            (float)stack.ChromaticAberration
        };
        ReadOnlySpan<float> vignetteColor = stackalloc float[]
        {
            (float)stack.VignetteColor.r, (float)stack.VignetteColor.g,
            (float)stack.VignetteColor.b, (float)stack.VignetteColor.a
        };
        ReadOnlySpan<float> bloom = stackalloc float[]
        {
            (float)stack.BloomIntensity, (float)stack.BloomThreshold,
            Math.Max(0.5f, (float)stack.BloomScatter * 4f), 0f
        };
        ReadOnlySpan<float> texture = stackalloc float[]
        {
            1f / Math.Max(1, sourceWidth), 1f / Math.Max(1, sourceHeight),
            stopNaNPropagation ? 1f : 0f, (float)stack.FilmGrain
        };
        // Only position/UV vary: every other attribute must remain identical
        // across this quad because the shaders use flat effect parameters.
        AddVertex(0, 0, 0, 0, color, filter, vignette, vignetteColor, bloom, texture);
        AddVertex(width, 0, 1, 0, color, filter, vignette, vignetteColor, bloom, texture);
        AddVertex(width, height, 1, 1, color, filter, vignette, vignetteColor, bloom, texture);
        AddVertex(0, 0, 0, 0, color, filter, vignette, vignetteColor, bloom, texture);
        AddVertex(width, height, 1, 1, color, filter, vignette, vignetteColor, bloom, texture);
        AddVertex(0, height, 0, 1, color, filter, vignette, vignetteColor, bloom, texture);
    }

    private void AddVertex(float x, float y, float u, float v,
        ReadOnlySpan<float> color, ReadOnlySpan<float> filter, ReadOnlySpan<float> vignette, ReadOnlySpan<float> vignetteColor,
        ReadOnlySpan<float> bloom, ReadOnlySpan<float> texture)
    {
        _vertices.Add(x); _vertices.Add(y); _vertices.Add(u); _vertices.Add(v);
        _vertices.AddRange(color); _vertices.AddRange(filter); _vertices.AddRange(vignette);
        _vertices.AddRange(vignetteColor); _vertices.AddRange(bloom); _vertices.AddRange(texture);
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaders(
        IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return Sources(GraphicsShaderLanguage.Glsl,
                "Shaders/PostProcessing/PostProcess.vulkan.vert.glsl",
                "Shaders/PostProcessing/PostProcess.vulkan.frag.glsl");
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return Sources(GraphicsShaderLanguage.Glsl,
                "Shaders/PostProcessing/PostProcess.opengl.vert.glsl",
                "Shaders/PostProcessing/PostProcess.opengl.frag.glsl");
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return Sources(GraphicsShaderLanguage.Hlsl,
                "Shaders/PostProcessing/PostProcess.direct3d.vert.hlsl",
                "Shaders/PostProcessing/PostProcess.direct3d.frag.hlsl");
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var shader = DefaultShaderResources.Load("Shaders/PostProcessing/PostProcess.webgpu.wgsl");
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, shader, "vs_main"),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, shader, "fs_main"));
        }
        throw new NotSupportedException($"Post Processing has no shader source for {device.Backend}.");
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) Sources(
        GraphicsShaderLanguage language, string vertex, string fragment) =>
        (new GraphicsShaderSource(GraphicsShaderStage.Vertex, language, DefaultShaderResources.Load(vertex)),
         new GraphicsShaderSource(GraphicsShaderStage.Fragment, language, DefaultShaderResources.Load(fragment)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _uploads.Dispose();
        _mesh.Dispose();
        foreach (var program in _programs) program.Dispose();
    }
}
