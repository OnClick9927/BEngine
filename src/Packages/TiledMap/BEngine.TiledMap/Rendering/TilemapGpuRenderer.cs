using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.TiledMap;

internal sealed class TilemapGpuRenderer : IDisposable
{
    private static readonly GraphicsVertexLayout VertexLayout = new(
        8 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
        ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _mesh;
    private readonly GraphicsMeshUploadCache _uploads;
    private readonly TilemapTextureCache _textures;
    private readonly List<float> _vertices = [];
    private bool _disposed;

    internal TilemapGpuRenderer(IGraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        device.Capabilities.Require(GraphicsDeviceFeatures.Rasterization |
                                    GraphicsDeviceFeatures.ShaderPrograms |
                                    GraphicsDeviceFeatures.DynamicVertexBuffers |
                                    GraphicsDeviceFeatures.SampledTextures |
                                    GraphicsDeviceFeatures.AlphaBlending);
        var (vertex, fragment) = ResolveShaders(device);
        _program = device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.TiledMap", vertex, fragment));
        _mesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.TiledMap.DynamicMesh", ReadOnlyMemory<float>.Empty, VertexLayout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
        _uploads = new GraphicsMeshUploadCache(_mesh);
        _textures = new TilemapTextureCache(device);
    }

    internal void Render(ReadOnlySpan<RenderSubmission2D> submissions, RenderCamera camera,
        int width, int height, GraphicsRect viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (submissions.Length == 0) return;
        var vertices = _vertices;
        vertices.Clear();
        vertices.EnsureCapacity(checked(submissions.Length * 96));
        using var tileBudget = LoopExecutionGuard.AllowIterations(submissions.Length + 1);
        var projection = camera.CreateViewportTransform(width, height);
        var opaque = true;
        foreach (var submission in submissions)
        {
            var tile = (TilemapRenderPayload)submission.Payload;
            opaque &= tile.Color.a == Fix64.One;
            AddQuad(vertices, tile, camera, projection);
        }
        _uploads.Update(CollectionsMarshal.AsSpan(vertices));
        var texture = _textures.Resolve(((TilemapRenderPayload)submissions[0].Payload).Texture, out var opaqueTexture);
        _device.SetViewport(viewport);
        _device.SetScissor(new GraphicsRect(0, 0, viewport.Width, viewport.Height));
        _device.SetDepthState(GraphicsDepthState.Disabled);
        try
        {
            _device.SetBlendMode(opaque && opaqueTexture ? GraphicsBlendMode.Disabled : GraphicsBlendMode.AlphaBlend);
            _device.SetRasterizerState(GraphicsRasterizerState.CullBackFaces);
            _program.Bind();
            _program.SetFloat("uViewportWidth", width);
            _program.SetFloat("uViewportHeight", height);
            _program.SetInt("uTexture", 0);
            _device.BindTexture(0, texture);
            _device.Draw(_mesh);
        }
        finally
        {
            // Keep the incoming state seen by subsequent contributors unchanged.
            _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
            _device.SetScissor(null);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _textures.Dispose();
        _uploads.Dispose();
        _mesh.Dispose();
        _program.Dispose();
        _disposed = true;
    }

    private static void AddQuad(List<float> output, TilemapRenderPayload tile,
        RenderCamera camera, RenderViewportTransform2D projection)
    {
        var winding = tile.AxisX.x * tile.AxisY.y - tile.AxisX.y * tile.AxisY.x;
        if (winding == 0) return;
        Span<Vector2> local = stackalloc Vector2[6];
        var count = TilemapGeometry.GetOutline(tile.Layout, Vector2.one, local, tile.HexSideRatio ?? Fix64.Half);
        Span<NVector2> points = stackalloc NVector2[6];
        var center = camera.SnapWorldPoint(tile.Center);
        for (var index = 0; index < count; index++)
            points[index] = projection.WorldToViewport(
                center + tile.AxisX * local[index].x + tile.AxisY * local[index].y);
        var u0 = (float)tile.Uv.x;
        var u1 = (float)tile.Uv.xMax;
        var v0 = (float)tile.Uv.y;
        var v1 = (float)tile.Uv.yMax;
        if (tile.FlipX) (u0, u1) = (u1, u0);
        if (tile.FlipY) (v0, v1) = (v1, v0);
        Span<NVector2> uv = stackalloc NVector2[6];
        for (var index = 0; index < count; index++)
            uv[index] = new NVector2(u0 + (u1 - u0) * (float)(local[index].x + Fix64.Half),
                v1 + (v0 - v1) * (float)(local[index].y + Fix64.Half));
        var color = new NVector4((float)tile.Color.r, (float)tile.Color.g,
            (float)tile.Color.b, (float)tile.Color.a);
        for (var index = 1; index < count - 1; index++)
        {
            var a = winding > 0 ? index : index + 1; var b = winding > 0 ? index + 1 : index;
            AddTriangle(output, points[0], points[a], points[b], uv[0], uv[a], uv[b], color);
        }
    }

    private static void AddTriangle(List<float> output, NVector2 a, NVector2 b, NVector2 c,
        NVector2 uvA, NVector2 uvB, NVector2 uvC, NVector4 color)
    {
        AddVertex(output, a, uvA, color);
        AddVertex(output, b, uvB, color);
        AddVertex(output, c, uvC, color);
    }

    private static void AddVertex(List<float> output, NVector2 point, NVector2 uv, NVector4 color)
    {
        output.Add(point.X); output.Add(point.Y);
        output.Add(color.X); output.Add(color.Y); output.Add(color.Z); output.Add(color.W);
        output.Add(uv.X); output.Add(uv.Y);
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaders(
        IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/TiledMap/Textured.vulkan.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/TiledMap/Textured.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/TiledMap/Textured.opengl.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/TiledMap/Textured.opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/TiledMap/Textured.direct3d.vert.hlsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/TiledMap/Textured.direct3d.frag.hlsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var shader = DefaultShaderResources.Load("Shaders/TiledMap/Textured.webgpu.wgsl");
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, shader, "vs_main"),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, shader, "fs_main"));
        }
        throw new NotSupportedException($"TiledMap has no shader source for {device.Backend}.");
    }
}
