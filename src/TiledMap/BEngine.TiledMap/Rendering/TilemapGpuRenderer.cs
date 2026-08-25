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
    private readonly TilemapTextureCache _textures;
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
        _textures = new TilemapTextureCache(device);
    }

    internal void Render(IEnumerable<TilemapRenderPayload> payloads, RenderCamera camera,
        int width, int height, GraphicsRect viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tiles = payloads.ToArray();
        if (tiles.Length == 0) return;
        var vertices = new List<float>(checked(tiles.Length * 48));
        foreach (var tile in tiles) AddQuad(vertices, tile, camera, width, height);
        _mesh.Update(CollectionsMarshal.AsSpan(vertices));
        _device.SetViewport(viewport);
        _device.SetScissor(new GraphicsRect(0, 0, viewport.Width, viewport.Height));
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.CullBackFaces);
        _program.Bind();
        _program.SetFloat("uViewportWidth", width);
        _program.SetFloat("uViewportHeight", height);
        _program.SetInt("uTexture", 0);
        _device.BindTexture(0, _textures.Resolve(tiles[0].Texture));
        _device.Draw(_mesh);
        _device.SetScissor(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _textures.Dispose();
        _mesh.Dispose();
        _program.Dispose();
        _disposed = true;
    }

    private static void AddQuad(List<float> output, TilemapRenderPayload tile,
        RenderCamera camera, int width, int height)
    {
        var half = tile.Size * Fix64.Half;
        var local = new[]
        {
            new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y),
            new Vector2(half.x, half.y), new Vector2(-half.x, half.y)
        };
        var points = local.Select(point => camera.WorldToViewport(
            tile.Center + TilemapMath2D.Rotate(point, tile.Rotation), width, height)).ToArray();
        var u0 = (float)tile.Uv.x;
        var u1 = (float)tile.Uv.xMax;
        var v0 = (float)tile.Uv.y;
        var v1 = (float)tile.Uv.yMax;
        if (tile.FlipX) (u0, u1) = (u1, u0);
        if (tile.FlipY) (v0, v1) = (v1, v0);
        var uv = new[]
        {
            new NVector2(u0, v1), new NVector2(u1, v1),
            new NVector2(u1, v0), new NVector2(u0, v0)
        };
        var color = new NVector4((float)tile.Color.r, (float)tile.Color.g,
            (float)tile.Color.b, (float)tile.Color.a);
        AddTriangle(output, points[0], points[1], points[2], uv[0], uv[1], uv[2], color);
        AddTriangle(output, points[0], points[2], points[3], uv[0], uv[2], uv[3], color);
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
