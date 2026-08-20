using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed class PortableSceneRenderer : IDisposable
{
    private static readonly GraphicsVertexLayout VertexLayout = new(
        6 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float))
        ]);
    private static readonly GraphicsVertexLayout TexturedVertexLayout = new(
        8 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
        ]);

    private readonly IGraphicsDevice _device;
    private readonly bool _ownsDevice;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _triangleMesh;
    private readonly IGraphicsMesh _lineMesh;
    private readonly SceneTextureCache _textures;
    private IGraphicsProgram? _texturedProgram;
    private IGraphicsMesh? _texturedMesh;
    private bool _disposed;

    public GraphicsBackend Backend => _device.Backend;
    public GraphicsDeviceCapabilities Capabilities => _device.Capabilities;

    public PortableSceneRenderer(IGraphicsDevice device, bool ownsDevice = false)
    {
        MainThreadGuard.Ensure("Create PortableSceneRenderer");
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _ownsDevice = ownsDevice;
        _device.Capabilities.Require(GraphicsDeviceFeatures.Rasterization |
                                     GraphicsDeviceFeatures.ShaderPrograms |
                                     GraphicsDeviceFeatures.DynamicVertexBuffers |
                                     GraphicsDeviceFeatures.AlphaBlending);
        var (vertex, fragment) = ResolveShaders(device);
        _program = device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.Scene2D", vertex, fragment));
        _triangleMesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.Scene2D.Triangles", ReadOnlyMemory<float>.Empty, VertexLayout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
        _lineMesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.Scene2D.Lines", ReadOnlyMemory<float>.Empty, VertexLayout,
            GraphicsPrimitiveTopology.LineList, GraphicsBufferUsage.Dynamic));
        _textures = new SceneTextureCache(device);
    }

    public void Render(Scene scene, RenderCamera camera, int width, int height,
        bool drawGrid = false, bool drawUi = true, bool drawExtensions = true, bool drawGizmos = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Render([scene], scene, camera, width, height, drawGrid, drawUi, drawExtensions, drawGizmos);
    }

    public void Render(IReadOnlyList<Scene> scenes, Scene activeScene, RenderCamera camera, int width, int height,
        bool drawGrid = false, bool drawUi = true, bool drawExtensions = true, bool drawGizmos = false)
    {
        RenderCore(scenes, activeScene, camera,
            new GraphicsRect(0, 0, Math.Max(1, width), Math.Max(1, height)),
            clearTarget: true, initializeColor: false, drawGrid, drawUi, drawGizmos);
    }

    public void RenderViewport(IReadOnlyList<Scene> scenes, Scene activeScene, RenderCamera camera,
        GraphicsRect viewport, bool initializeColor = false, bool drawGrid = false, bool drawUi = true,
        bool drawExtensions = true, bool drawGizmos = false)
    {
        RenderCore(scenes, activeScene, camera, viewport, clearTarget: false, initializeColor,
            drawGrid, drawUi, drawGizmos);
    }

    public void FillViewport(GraphicsRect viewport, NVector4 color)
    {
        MainThreadGuard.Ensure();
        ObjectDisposedException.ThrowIf(_disposed, this);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        PrepareViewport(viewport);
        DrawVertices(_triangleMesh, CreateScreenQuad(0, 0, viewport.Width, viewport.Height, color),
            viewport.Width, viewport.Height);
        _device.SetScissor(null);
    }

    private void RenderCore(IReadOnlyList<Scene> scenes, Scene activeScene, RenderCamera camera,
        GraphicsRect viewport, bool clearTarget, bool initializeColor, bool drawGrid, bool drawUi, bool drawGizmos)
    {
        MainThreadGuard.Ensure();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(activeScene);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        PrepareViewport(viewport);
        if (clearTarget)
            _device.Clear(GraphicsClearFlags.Color, camera.ClearColor);
        else if (initializeColor)
            DrawVertices(_triangleMesh,
                CreateScreenQuad(0, 0, viewport.Width, viewport.Height, camera.ClearColor),
                viewport.Width, viewport.Height);

        if (drawGrid) DrawGrid(camera, viewport.Width, viewport.Height);

        var submissions = Collect(scenes, camera, viewport.Width, viewport.Height, drawUi);
        foreach (var batch in RenderBatchBuilder2D.Build(submissions))
        {
            if (SceneRenderContributor2DRegistry.TryRender(
                    _device, batch, camera, viewport.Width, viewport.Height, viewport))
            {
                PrepareViewport(viewport);
                continue;
            }
            DrawBatch(batch, camera, viewport.Width, viewport.Height);
        }

        if (drawGizmos) DrawCameraGizmos(scenes, camera, viewport.Width, viewport.Height);
        _device.SetScissor(null);
    }

    private IReadOnlyList<RenderSubmission2D> Collect(
        IReadOnlyList<Scene> scenes, RenderCamera camera, int width, int height, bool drawUi)
    {
        var submissions = new List<RenderSubmission2D>();
        long sequence = 0;
        foreach (var scene in scenes)
        {
            if (scene is null || !scene.isLoaded) continue;
            var hierarchy = HierarchyOrder2D.Build(scene);
            foreach (var renderer in scene.QueryComponents<SpriteRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var color = WithOpacity(renderer.color, renderer.opacity);
                var visual = renderer.ResolveSpriteUnchecked();
                var pivot = renderer.useAtlasPivot && !string.IsNullOrWhiteSpace(renderer.atlas)
                    ? visual.Pivot
                    : renderer.pivot;
                var localCenter = new Vector2(
                    (Fix64.Half - pivot.x) * renderer.size.x,
                    (Fix64.Half - pivot.y) * renderer.size.y);
                submissions.Add(new RenderSubmission2D(
                    new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                        hierarchy.GetValueOrDefault(renderer.gameObject), renderer.TransparencyUnchecked(color),
                        sequence++),
                    renderer.BatchKey(visual),
                    new Quad(renderer.transform.TransformPoint(localCenter), renderer.transform.rotation,
                        Vector2.Scale(renderer.size, renderer.transform.lossyScale), color,
                        visual.Texture, visual.Uv, renderer.flipX, renderer.flipY, visual.IsTextured)));
            }
            foreach (var system in scene.QueryComponents<ParticleSystem2D>())
            {
                if (!system.enabled || !system.gameObject.activeInHierarchy) continue;
                var hierarchyOrder = hierarchy.GetValueOrDefault(system.gameObject);
                var visual = system.ResolveSpriteUnchecked();
                foreach (var particle in system.particles)
                {
                    var worldPosition = system.transform.TransformPoint(particle.Position);
                    var color = WithOpacity(particle.Color, system.opacity);
                    var rotation = system.transform.rotation + particle.Rotation;
                    var offset = new Vector2(
                        (Fix64.Half - visual.Pivot.x) * particle.Size.x,
                        (Fix64.Half - visual.Pivot.y) * particle.Size.y);
                    worldPosition += Transform.RotateVector(
                        Vector2.Scale(offset, system.transform.lossyScale), rotation);
                    submissions.Add(new RenderSubmission2D(
                        new RenderSortKey2D(system.sortingLayer, system.orderInLayer, hierarchyOrder,
                            system.TransparencyUnchecked(color), sequence++),
                        system.BatchKey(visual),
                        new Quad(worldPosition, rotation,
                            Vector2.Scale(particle.Size, system.transform.lossyScale), color,
                            visual.Texture, visual.Uv, false, false, visual.IsTextured)));
                }
            }
            if (drawUi)
                sequence = SceneRenderContributor2DRegistry.Collect(
                    _device, scene, camera, width, height, submissions, sequence);
        }
        return submissions;
    }

    private void DrawBatch(RenderBatch2D batch, RenderCamera camera, int width, int height)
    {
        var quads = batch.Submissions.Select(submission => submission.Payload)
            .OfType<Quad>().ToArray();
        if (quads.Length == 0) return;
        if (quads[0].IsTextured)
        {
            EnsureTexturedPipeline();
            var vertices = new List<float>(quads.Length * 48);
            foreach (var quad in quads) AddTexturedQuad(vertices, quad, camera, width, height);
            DrawTexturedVertices(vertices, quads[0].Texture, width, height);
            return;
        }
        var solidVertices = new List<float>(quads.Length * 36);
        foreach (var quad in quads) AddQuad(solidVertices, quad, camera, width, height);
        DrawVertices(_triangleMesh, solidVertices, width, height);
    }

    private void DrawGrid(RenderCamera camera, int width, int height)
    {
        var lines = new List<float>();
        var halfWidth = camera.Size * width / Math.Max(1, height);
        var left = (int)(camera.Position.x - halfWidth) - 1;
        var right = (int)(camera.Position.x + halfWidth) + 1;
        var bottom = (int)(camera.Position.y - camera.Size) - 1;
        var top = (int)(camera.Position.y + camera.Size) + 1;
        var minor = new NVector4(0.27f, 0.29f, 0.32f, 0.65f);
        var axis = new NVector4(0.42f, 0.44f, 0.48f, 0.9f);
        for (var x = left; x <= right; x++)
            AddLine(lines, camera.WorldToViewport(new Vector2(x, bottom), width, height),
                camera.WorldToViewport(new Vector2(x, top), width, height), x == 0 ? axis : minor);
        for (var y = bottom; y <= top; y++)
            AddLine(lines, camera.WorldToViewport(new Vector2(left, y), width, height),
                camera.WorldToViewport(new Vector2(right, y), width, height), y == 0 ? axis : minor);
        if (lines.Count > 0) DrawVertices(_lineMesh, lines, width, height);
    }

    private void DrawCameraGizmos(IReadOnlyList<Scene> scenes, RenderCamera view, int width, int height)
    {
        var lines = new List<float>();
        foreach (var scene in scenes)
        foreach (var camera in scene.QueryComponents<Camera2D>())
        {
            if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
            var aspect = width / (Fix64)Math.Max(1, height);
            var half = new Vector2(camera.size * aspect, camera.size);
            var center = camera.transform.position;
            var points = new[]
            {
                center + new Vector2(-half.x, -half.y), center + new Vector2(half.x, -half.y),
                center + new Vector2(half.x, half.y), center + new Vector2(-half.x, half.y)
            };
            for (var index = 0; index < points.Length; index++)
                AddLine(lines, view.WorldToViewport(points[index], width, height),
                    view.WorldToViewport(points[(index + 1) % points.Length], width, height),
                    new NVector4(1f, 0.72f, 0.12f, 1f));
        }
        if (lines.Count > 0) DrawVertices(_lineMesh, lines, width, height);
    }

    private void PrepareViewport(GraphicsRect viewport)
    {
        _device.SetViewport(viewport);
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        _device.SetScissor(new GraphicsRect(0, 0, viewport.Width, viewport.Height));
    }

    private void DrawVertices(IGraphicsMesh mesh, List<float> vertices, int width, int height)
    {
        mesh.Update(CollectionsMarshal.AsSpan(vertices));
        _program.Bind();
        _program.SetFloat("uViewportWidth", width);
        _program.SetFloat("uViewportHeight", height);
        _device.Draw(mesh);
    }

    private void DrawTexturedVertices(List<float> vertices, string texture, int width, int height)
    {
        _texturedMesh!.Update(CollectionsMarshal.AsSpan(vertices));
        _texturedProgram!.Bind();
        _texturedProgram.SetFloat("uViewportWidth", width);
        _texturedProgram.SetFloat("uViewportHeight", height);
        _texturedProgram.SetInt("uTexture", 0);
        _device.BindTexture(0, _textures.Resolve(texture));
        _device.Draw(_texturedMesh);
    }

    private static void AddQuad(List<float> output, Quad quad, RenderCamera camera, int width, int height)
    {
        var half = quad.Size * Fix64.Half;
        var local = new[]
        {
            new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y),
            new Vector2(half.x, half.y), new Vector2(-half.x, half.y)
        };
        var points = local.Select(point => camera.WorldToViewport(
            quad.Center + Transform.RotateVector(point, quad.Rotation), width, height)).ToArray();
        var color = Numerics.ToNumerics(quad.Color);
        AddTriangle(output, points[0], points[1], points[2], color);
        AddTriangle(output, points[0], points[2], points[3], color);
    }

    private static void AddTexturedQuad(
        List<float> output, Quad quad, RenderCamera camera, int width, int height)
    {
        var half = quad.Size * Fix64.Half;
        var local = new[]
        {
            new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y),
            new Vector2(half.x, half.y), new Vector2(-half.x, half.y)
        };
        var points = local.Select(point => camera.WorldToViewport(
            quad.Center + Transform.RotateVector(point, quad.Rotation), width, height)).ToArray();
        var u0 = (float)quad.Uv.x;
        var u1 = (float)quad.Uv.xMax;
        var v0 = (float)quad.Uv.y;
        var v1 = (float)quad.Uv.yMax;
        if (quad.FlipX) (u0, u1) = (u1, u0);
        if (quad.FlipY) (v0, v1) = (v1, v0);
        var uv = new[]
        {
            new NVector2(u0, v1), new NVector2(u1, v1),
            new NVector2(u1, v0), new NVector2(u0, v0)
        };
        var color = Numerics.ToNumerics(quad.Color);
        AddTexturedTriangle(output, points[0], points[1], points[2], uv[0], uv[1], uv[2], color);
        AddTexturedTriangle(output, points[0], points[2], points[3], uv[0], uv[2], uv[3], color);
    }

    private static List<float> CreateScreenQuad(float x, float y, float width, float height, NVector4 color)
    {
        var result = new List<float>(36);
        var a = new NVector2(x, y);
        var b = new NVector2(x + width, y);
        var c = new NVector2(x + width, y + height);
        var d = new NVector2(x, y + height);
        AddTriangle(result, a, b, c, color);
        AddTriangle(result, a, c, d, color);
        return result;
    }

    private static void AddTriangle(List<float> output, NVector2 a, NVector2 b, NVector2 c, NVector4 color)
    {
        AddVertex(output, a, color);
        AddVertex(output, b, color);
        AddVertex(output, c, color);
    }
    private static void AddLine(List<float> output, NVector2 a, NVector2 b, NVector4 color)
    {
        AddVertex(output, a, color);
        AddVertex(output, b, color);
    }
    private static void AddTexturedTriangle(List<float> output,
        NVector2 a, NVector2 b, NVector2 c,
        NVector2 uvA, NVector2 uvB, NVector2 uvC, NVector4 color)
    {
        AddTexturedVertex(output, a, uvA, color);
        AddTexturedVertex(output, b, uvB, color);
        AddTexturedVertex(output, c, uvC, color);
    }
    private static void AddVertex(List<float> output, NVector2 point, NVector4 color)
    {
        output.Add(point.X); output.Add(point.Y);
        output.Add(color.X); output.Add(color.Y); output.Add(color.Z); output.Add(color.W);
    }
    private static void AddTexturedVertex(List<float> output, NVector2 point, NVector2 uv, NVector4 color)
    {
        AddVertex(output, point, color);
        output.Add(uv.X); output.Add(uv.Y);
    }
    private static Color WithOpacity(Color color, Fix64 opacity) =>
        new(color.r, color.g, color.b, color.a * opacity);

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaders(IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Solid.vulkan.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Solid.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Solid.opengl.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Solid.opengl.frag.glsl")));
        throw new NotSupportedException($"The 2D scene renderer has no shader source for {device.Backend}.");
    }

    private void EnsureTexturedPipeline()
    {
        if (_texturedProgram is not null) return;
        _device.Capabilities.Require(GraphicsDeviceFeatures.SampledTextures);
        var (vertex, fragment) = ResolveTexturedShaders(_device);
        _texturedProgram = _device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.Scene2D.Texture", vertex, fragment));
        _texturedMesh = _device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.Scene2D.TexturedTriangles", ReadOnlyMemory<float>.Empty, TexturedVertexLayout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveTexturedShaders(
        IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.vulkan.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.opengl.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.direct3d.vert.hlsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.direct3d.frag.hlsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var shader = DefaultShaderResources.Load("Shaders/PortableScene/Textured.webgpu.wgsl");
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, shader, "vs_main"),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, shader, "fs_main"));
        }
        throw new NotSupportedException($"The 2D scene renderer has no textured shader for {device.Backend}.");
    }

    public void Dispose()
    {
        MainThreadGuard.Ensure();
        if (_disposed) return;
        SceneRenderContributor2DRegistry.Release(_device);
        _textures.Dispose();
        _texturedMesh?.Dispose();
        _texturedProgram?.Dispose();
        _lineMesh.Dispose();
        _triangleMesh.Dispose();
        _program.Dispose();
        if (_ownsDevice) _device.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private readonly record struct Quad(
        Vector2 Center,
        Fix64 Rotation,
        Vector2 Size,
        Color Color,
        string Texture,
        Rect Uv,
        bool FlipX,
        bool FlipY,
        bool IsTextured);
}
