using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine;

internal sealed partial class SpriteSkinRenderContributor2D : ISceneRenderContributor2D
{
    private static readonly Guid MaterialId = new("62b65561-97bd-4ee2-bd36-ff886746aa8f");
    private static readonly GraphicsVertexLayout VertexLayout = new(8 * sizeof(float),
    [
        new GraphicsVertexAttribute(0, 2, 0),
        new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
        new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
    ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _mesh;
    private readonly GraphicsMeshUploadCache _meshUploads;
    private readonly List<SpriteSkinRenderPayload> _renderPayloads = [];
    private readonly SceneTextureCache _textures;
    private readonly List<float> _vertices = [];
    private bool _disposed;
    private SceneRenderContext2D? _context;
    private readonly BoundedLruCache<SpriteSkinAsset, StaticSkinData> _staticAssets = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SpriteSkinAsset> _preparedAssets = new(ReferenceEqualityComparer.Instance);
    public void BeginFrame() { _preparedAssets.Clear(); _staticAssets.Trim(128, 32 * 1024 * 1024); _textures.PruneDestroyedRuntimeTextures(); }
    private readonly List<SpriteSkin2D> _renderers = [];
    public void BeginScene(in SceneRenderContext2D context) => _context = context;
    public void EndFrame() { _context = null; _renderers.Clear(); _preparedAssets.Clear(); }


    internal SpriteSkinRenderContributor2D(IGraphicsDevice device)
    {
        _device = device;
        device.Capabilities.Require(GraphicsDeviceFeatures.Rasterization |
                                    GraphicsDeviceFeatures.ShaderPrograms |
                                    GraphicsDeviceFeatures.DynamicVertexBuffers |
                                    GraphicsDeviceFeatures.SampledTextures |
                                    GraphicsDeviceFeatures.AlphaBlending);
        var (vertex, fragment) = ResolveShaders(device);
        _program = device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.SpriteSkinning2D", vertex, fragment));
        _mesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.SpriteSkinning2D.DynamicMesh", ReadOnlyMemory<float>.Empty, VertexLayout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
        _meshUploads = new(_mesh);
        _textures = new SceneTextureCache(device);
    }

    public string packageId => "com.bengine.sprite-skinning2d";

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder) =>
        Collect(scene, camera, width, height, submissions, submissionOrder, null);

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder, Predicate<GameObject>? objectFilter)
    {
        var hierarchy = _context is { } context && ReferenceEquals(context.Scene, scene)
            ? context.HierarchyOrder : HierarchyOrder2D.Build(scene);
        scene.GetComponents(_renderers);
        foreach (var renderer in _renderers)
        {
            if (renderer.skin is null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                !camera.ContainsLayer(renderer.sortingLayer) || objectFilter is not null && !objectFilter(renderer.gameObject)) continue;
            StaticSkinData data;
            Vector2[] positions;
            SpriteSkinBoneTransform[]? palette = null;
            SpriteSkinAttachment attachment;
            try
            {
                data = PrepareAsset(renderer.skin);
                if (renderer.skinningMode != SpriteSkinningMode.Cpu && camera.Policy is null &&
                    _device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
                {
                    palette = renderer.GetWorldBoneTransformsValidated();
                    positions = data.Positions;
                    if (renderer.deformation.Length > 0)
                    {
                        positions = (Vector2[])positions.Clone();
                        for (var index = 0; index < Math.Min(positions.Length, renderer.deformation.Length); index++) positions[index] += renderer.deformation[index];
                    }
                }
                else positions = renderer.GetDeformedWorldVerticesValidated();
                attachment = renderer.ResolveAttachment();
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
            {
                continue;
            }
            if (!IsVisible(palette is null ? positions : ConservativeBounds(positions, palette), camera, width, height)) continue;
            var uv = data.ResolveUv(attachment.Uv);
            var color = renderer.color * attachment.Tint;
            color = new Color(color.r, color.g, color.b, color.a * renderer.opacity);
            submissions.Add(new RenderSubmission2D(
                new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                    hierarchy.GetValueOrDefault(renderer.gameObject), renderer.TransparencyUnchecked(color),
                    submissionOrder++),
                new RenderBatchKey2D(MaterialId, "BEngine/SpriteSkinning2D", attachment.Texture),
                new SpriteSkinRenderPayload(positions, uv, data.Triangles, color, attachment.Texture)
                {
                    Bones = palette,
                    Weights = palette is null ? [] : data.Weights,
                    AllowCpuFallback = renderer.skinningMode == SpriteSkinningMode.Auto
                }));
        }
        return submissionOrder;
    }

    private StaticSkinData PrepareAsset(SpriteSkinAsset asset)
    {
        if (!_staticAssets.TryGetValue(asset, out var data)) data = new();
        if (_preparedAssets.Add(asset))
        {
            asset.Validate(); data.Refresh(asset);
            _staticAssets.Remove(asset, out _);
            _staticAssets.Add(asset, data, data.EstimatedBytes);
        }
        return data;
    }

    private sealed class StaticSkinData
    {
        internal Vector2[] Positions = [], Uv = [];
        internal int[] Triangles = [];
        internal SpriteSkinWeight[][] Weights = [];
        private readonly Dictionary<Rect, Vector2[]> _uvRegions = [];
        internal long EstimatedBytes => Positions.Length * 640L + Triangles.Length * 4L;
        internal void Refresh(SpriteSkinAsset asset)
        {
            var changed = Positions.Length != asset.Vertices.Count;
            if (!changed)
                for (var i = 0; i < Positions.Length && !changed; i++)
                {
                    var source = asset.Vertices[i];
                    changed = Positions[i] != source.Position || Uv[i] != source.Uv || Weights[i].Length != source.Weights.Count;
                    for (var j = 0; j < Weights[i].Length && !changed; j++)
                        changed = Weights[i][j].BoneIndex != source.Weights[j].BoneIndex || Weights[i][j].Weight != source.Weights[j].Weight;
                }
            if (changed)
            {
                Positions = new Vector2[asset.Vertices.Count]; Uv = new Vector2[Positions.Length]; Weights = new SpriteSkinWeight[Positions.Length][];
                for (var i = 0; i < Positions.Length; i++)
                {
                    var source = asset.Vertices[i]; Positions[i] = source.Position; Uv[i] = source.Uv;
                    Weights[i] = source.Weights.Select(weight => new SpriteSkinWeight { BoneIndex = weight.BoneIndex, Weight = weight.Weight }).ToArray();
                }
                _uvRegions.Clear();
            }
            if (!CollectionsMarshal.AsSpan(asset.Triangles).SequenceEqual(Triangles)) Triangles = asset.Triangles.ToArray();
        }
        internal Vector2[] ResolveUv(Rect region)
        {
            if (_uvRegions.TryGetValue(region, out var cached)) return cached;
            if (_uvRegions.Count >= 16) _uvRegions.Clear();
            var result = new Vector2[Uv.Length];
            for (var i = 0; i < result.Length; i++) result[i] = new(region.x + Uv[i].x * region.width, region.y + Uv[i].y * region.height);
            _uvRegions.Add(region, result); return result;
        }
    }

    public bool CanRender(RenderBatch2D batch) => batch.HasOnlyPayloads<SpriteSkinRenderPayload>();

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _renderPayloads.Clear(); var gpu = false;
        foreach (var submission in batch.SubmissionSpan)
        {
            var payload = (SpriteSkinRenderPayload)submission.Payload;
            _renderPayloads.Add(payload); gpu |= payload.Bones is not null;
        }
        try
        {
            var payloads = CollectionsMarshal.AsSpan(_renderPayloads);
            if (payloads.IsEmpty) return;
            if (gpu)
            {
                foreach (var payload in payloads)
                if (payload.Bones is not null)
                {
                    try { RenderGpu(payload, camera, width, height, viewport); }
                    catch (Exception error) when (payload.AllowCpuFallback && error is InvalidOperationException or NotSupportedException)
                    { RenderCpu([ToCpu(payload)], camera, width, height, viewport); }
                }
                else RenderCpu([payload], camera, width, height, viewport);
            }
            else RenderCpu(payloads, camera, width, height, viewport);
        }
        finally { _renderPayloads.Clear(); }
    }

    private static SpriteSkinRenderPayload ToCpu(SpriteSkinRenderPayload payload) => payload with
    {
        Vertices = payload.Vertices.Select((position, index) => payload.Weights[index].Aggregate(Vector2.zero,
            (sum, influence) => sum + payload.Bones![influence.BoneIndex].TransformPoint(position) * influence.Weight)).ToArray(),
        Bones = null
    };

    private void RenderCpu(ReadOnlySpan<SpriteSkinRenderPayload> payloads, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        _vertices.Clear();
        foreach (var payload in payloads) AddMesh(payload, camera, width, height);
        if (_vertices.Count == 0) return;
        _meshUploads.Update(CollectionsMarshal.AsSpan(_vertices));
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
        foreach (var pipeline in _gpuPipelines.Values) { pipeline.Uploads.Dispose(); pipeline.Mesh.Dispose(); pipeline.Program.Dispose(); }
        _gpuPipelines.Clear();
        EndFrame(); _staticAssets.Clear();
        _textures.Dispose(); _meshUploads.Dispose(); _mesh.Dispose(); _program.Dispose(); _disposed = true;
    }

    private static Vector2[] ConservativeBounds(Vector2[] positions, SpriteSkinBoneTransform[] palette)
    {
        var min = positions.Aggregate(Vector2.Min); var max = positions.Aggregate(Vector2.Max);
        Vector2[] corners = [min, new(max.x, min.y), max, new(min.x, max.y)];
        var first = true; var worldMin = Vector2.zero; var worldMax = Vector2.zero;
        foreach (var bone in palette) foreach (var corner in corners)
        {
            var point = bone.TransformPoint(corner);
            if (first) { worldMin = worldMax = point; first = false; }
            else { worldMin = Vector2.Min(worldMin, point); worldMax = Vector2.Max(worldMax, point); }
        }
        return [worldMin, worldMax];
    }

    private void AddMesh(SpriteSkinRenderPayload payload, RenderCamera camera, int width, int height)
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
        var center = (min + max) * Fix64.Half; var size = max - min;
        return camera.IsVisible(center, 0, size, width, height);
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaders(IGraphicsDevice device)
    {
        const string root = "Shaders/SpriteSkinning2D/Textured";
        if (device.Backend is GraphicsBackend.Vulkan or GraphicsBackend.Metal)
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(root + ".vulkan.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(root + ".vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(root + ".opengl.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(root + ".opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load(root + ".direct3d.vert.hlsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load(root + ".direct3d.frag.hlsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var shader = DefaultShaderResources.Load(root + ".webgpu.wgsl");
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, shader, "vs_main"),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, shader, "fs_main"));
        }
        throw new NotSupportedException($"SpriteSkinning2D has no shader source for {device.Backend}.");
    }
}
