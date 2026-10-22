using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine;

internal sealed class SpriteShapeRenderContributor2D : ISceneRenderContributor2D
{
    private static readonly Guid FillMaterialId = new("356b8349-58f0-45e4-a49b-63fe6a4ca45e");
    private static readonly Guid EdgeMaterialId = new("ad447a95-72fc-4c42-8894-ce644a28e5f1");
    private static readonly GraphicsVertexLayout VertexLayout = new(8 * sizeof(float),
    [
        new GraphicsVertexAttribute(0, 2, 0),
        new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
        new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
    ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _mesh;
    private readonly SceneTextureCache _textures;
    private readonly List<float> _vertices = [];
    private bool _disposed;

    internal SpriteShapeRenderContributor2D(IGraphicsDevice device)
    {
        _device = device;
        device.Capabilities.Require(GraphicsDeviceFeatures.Rasterization |
                                    GraphicsDeviceFeatures.ShaderPrograms |
                                    GraphicsDeviceFeatures.DynamicVertexBuffers |
                                    GraphicsDeviceFeatures.SampledTextures |
                                    GraphicsDeviceFeatures.AlphaBlending);
        var (vertex, fragment) = ResolveShaders(device);
        _program = device.CreateProgram(new GraphicsShaderProgramDescription("BEngine.SpriteShape2D", vertex, fragment));
        _mesh = device.CreateMesh(new GraphicsMeshDescription("BEngine.SpriteShape2D.DynamicMesh",
            ReadOnlyMemory<float>.Empty, VertexLayout, GraphicsPrimitiveTopology.TriangleList,
            GraphicsBufferUsage.Dynamic));
        _textures = new SceneTextureCache(device);
    }

    public string packageId => "com.bengine.sprite-shape2d";

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder) =>
        Collect(scene, camera, width, height, submissions, submissionOrder, null);

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder, Predicate<GameObject>? objectFilter)
    {
        var hierarchy = HierarchyOrder2D.Build(scene);
        foreach (var renderer in scene.QueryComponents<SpriteShapeRenderer2D>()
                     .Where(item => item.enabled && item.gameObject.activeInHierarchy && item.shape is not null)
                     .Where(item => camera.ContainsLayer(item.sortingLayer))
                     .Where(item => objectFilter is null || objectFilter(item.gameObject))
                     .OrderBy(item => hierarchy.GetValueOrDefault(item.gameObject)))
        {
            SpriteShapeGeometry geometry;
            try { geometry = renderer.BuildGeometry(); }
            catch (InvalidDataException) { continue; }
            if (!IsVisible(geometry.Outline.Concat(geometry.EdgeVertices).Concat(geometry.Corners.SelectMany(corner => corner.Vertices))
                    .Select(renderer.transform.TransformPoint).ToArray(), camera, width, height)) continue;
            var asset = renderer.shape!;
            if (asset.Closed && geometry.FillTriangles.Length > 0)
                submissionOrder = Add(renderer, geometry.FillVertices, geometry.FillUv, geometry.FillTriangles,
                    asset.FillColor, asset.FillTexture, FillMaterialId, hierarchy, submissions, submissionOrder);
            if (geometry.EdgeTriangles.Length > 0)
                submissionOrder = Add(renderer, geometry.EdgeVertices, geometry.EdgeUv, geometry.EdgeTriangles,
                    asset.EdgeColor, asset.EdgeTexture, EdgeMaterialId, hierarchy, submissions, submissionOrder);
            foreach (var corner in geometry.Corners)
                submissionOrder = Add(renderer, corner.Vertices, corner.Uv, [0, 1, 2, 0, 2, 3],
                    corner.Color, corner.Texture, EdgeMaterialId, hierarchy, submissions, submissionOrder);
        }
        return submissionOrder;
    }

    public bool CanRender(RenderBatch2D batch) => batch.HasOnlyPayloads<SpriteShapeRenderPayload>();

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payloads = batch.Submissions.Select(item => (SpriteShapeRenderPayload)item.Payload).ToArray();
        if (payloads.Length == 0) return;
        _vertices.Clear();
        foreach (var payload in payloads) AddMesh(payload, camera, width, height);
        if (_vertices.Count == 0) return;
        _mesh.Update(CollectionsMarshal.AsSpan(_vertices));
        _device.SetViewport(viewport);
        _device.SetScissor(new GraphicsRect(0, 0, viewport.Width, viewport.Height));
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.CullBackFaces);
        _program.Bind();
        _program.SetFloat("uViewportWidth", width);
        _program.SetFloat("uViewportHeight", height);
        _program.SetInt("uTexture", 0);
        _device.BindTexture(0, _textures.Resolve(payloads[0].Texture));
        _device.Draw(_mesh);
        _device.SetScissor(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _textures.Dispose(); _mesh.Dispose(); _program.Dispose(); _disposed = true;
    }

    private static long Add(SpriteShapeRenderer2D renderer, IReadOnlyList<Vector2> positions,
        Vector2[] uv, int[] triangles, Color tint, string texture, Guid material,
        IReadOnlyDictionary<GameObject, long> hierarchy, ICollection<RenderSubmission2D> submissions, long order)
    {
        var world = positions.Select(renderer.transform.TransformPoint).ToArray();
        var color = renderer.color * tint;
        color = new Color(color.r, color.g, color.b, color.a * renderer.opacity);
        submissions.Add(new RenderSubmission2D(
            new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                hierarchy.GetValueOrDefault(renderer.gameObject), renderer.TransparencyUnchecked(color), order++),
            new RenderBatchKey2D(material, "BEngine/SpriteShape2D", texture),
            new SpriteShapeRenderPayload(world, uv, triangles, color, texture)));
        return order;
    }

    private void AddMesh(SpriteShapeRenderPayload payload, RenderCamera camera, int width, int height)
    {
        var color = new NVector4((float)payload.Color.r, (float)payload.Color.g,
            (float)payload.Color.b, (float)payload.Color.a);
        foreach (var index in payload.Triangles)
        {
            var point = camera.WorldToViewport(camera.SnapWorldPoint(payload.Vertices[index]), width, height);
            var uv = payload.Uv[index];
            _vertices.Add(point.X); _vertices.Add(point.Y);
            _vertices.Add(color.X); _vertices.Add(color.Y); _vertices.Add(color.Z); _vertices.Add(color.W);
            _vertices.Add((float)uv.x); _vertices.Add((float)uv.y);
        }
    }

    private static bool IsVisible(IReadOnlyList<Vector2> vertices, RenderCamera camera, int width, int height)
    {
        if (vertices.Count == 0) return false;
        var min = vertices.Aggregate(Vector2.Min); var max = vertices.Aggregate(Vector2.Max);
        return camera.IsVisible((min + max) * Fix64.Half, 0, max - min, width, height);
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaders(IGraphicsDevice device)
    {
        const string root = "Shaders/SpriteShape2D/Textured";
        if (device.Backend == GraphicsBackend.Vulkan)
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, DefaultShaderResources.Load(root + ".vulkan.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, DefaultShaderResources.Load(root + ".vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, DefaultShaderResources.Load(root + ".opengl.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, DefaultShaderResources.Load(root + ".opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl, DefaultShaderResources.Load(root + ".direct3d.vert.hlsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl, DefaultShaderResources.Load(root + ".direct3d.frag.hlsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var shader = DefaultShaderResources.Load(root + ".webgpu.wgsl");
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, shader, "vs_main"),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, shader, "fs_main"));
        }
        throw new NotSupportedException($"SpriteShape2D has no shader source for {device.Backend}.");
    }
}
