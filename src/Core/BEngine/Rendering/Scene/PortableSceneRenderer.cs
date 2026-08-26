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
    public SceneRenderStatistics LastRenderStatistics { get; private set; }

    public PortableSceneRenderer(IGraphicsDevice device, bool ownsDevice = false)
    {
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
        var drawStart = CaptureDrawStatistics(out var hasCompleteDrawStatistics);
        var work = RenderCore(scenes, activeScene, camera,
            new GraphicsRect(0, 0, Math.Max(1, width), Math.Max(1, height)),
            clearTarget: true, initializeColor: false, drawGrid, drawUi, drawExtensions, drawGizmos);
        LastRenderStatistics = CompleteStatistics(
            cameraCount: 1, work, Math.Max(1, width), Math.Max(1, height),
            drawStart, hasCompleteDrawStatistics);
    }

    public void RenderViewport(IReadOnlyList<Scene> scenes, Scene activeScene, RenderCamera camera,
        GraphicsRect viewport, bool initializeColor = false, bool drawGrid = false, bool drawUi = true,
        bool drawExtensions = true, bool drawGizmos = false,
        Predicate<GameObject>? objectFilter = null)
    {
        var drawStart = CaptureDrawStatistics(out var hasCompleteDrawStatistics);
        var work = RenderCore(scenes, activeScene, camera, viewport, clearTarget: false, initializeColor,
            drawGrid, drawUi, drawExtensions, drawGizmos, objectFilter: objectFilter);
        LastRenderStatistics = CompleteStatistics(
            cameraCount: 1, work, Math.Max(1, viewport.Width), Math.Max(1, viewport.Height),
            drawStart, hasCompleteDrawStatistics);
    }

    public void RenderCameras(IReadOnlyList<Scene> scenes, Scene activeScene,
        IReadOnlyList<Camera2D> cameras, GraphicsRect targetViewport, bool drawUi = true)
    {
        RenderCameras(scenes, activeScene, cameras, targetViewport,
            targetViewport.Width, targetViewport.Height, drawUi);
    }

    public void RenderCameras(IReadOnlyList<Scene> scenes, Scene activeScene,
        IReadOnlyList<Camera2D> cameras, GraphicsRect targetViewport,
        int targetWidth, int targetHeight, bool drawUi = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(activeScene);
        ArgumentNullException.ThrowIfNull(cameras);
        targetViewport.Validate();
        LastRenderStatistics = default;
        if (targetViewport.Width <= 0 || targetViewport.Height <= 0) return;
        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);
        var drawStart = CaptureDrawStatistics(out var hasCompleteDrawStatistics);
        var renderedCameraCount = 0;
        var visibleSubmissionCount = 0;
        var batchCount = 0;

        FillViewport(targetViewport, RenderCamera.Default.ClearColor);
        var ordered = cameras.Select((camera, sequence) => (Camera: camera, Sequence: sequence))
            .Where(item => item.Camera.enabled && item.Camera.gameObject.activeInHierarchy)
            .OrderBy(item => item.Camera.priority)
            .ThenBy(item => item.Sequence);
        foreach (var item in ordered)
        {
            var camera = RenderCamera.From(item.Camera);
            var viewport = ResolveViewport(targetViewport, camera.ViewportRect, _device.Backend);
            if (viewport.Width <= 0 || viewport.Height <= 0) continue;
            renderedCameraCount++;
            var logicalViewport = ResolveViewport(
                new GraphicsRect(0, 0, targetWidth, targetHeight),
                camera.ViewportRect, _device.Backend);
            switch (camera.ClearMode)
            {
                case CameraClearMode.Color:
                    FillViewport(viewport, camera.ClearColor);
                    ClearDepth(viewport);
                    break;
                case CameraClearMode.DepthOnly:
                    ClearDepth(viewport);
                    break;
                case CameraClearMode.Nothing:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(camera.ClearMode));
            }
            var work = RenderCore(scenes, activeScene, camera, viewport, clearTarget: false,
                initializeColor: false, drawGrid: false, drawUi,
                drawExtensions: true, drawGizmos: false,
                logicalWidth: Math.Max(1, logicalViewport.Width),
                logicalHeight: Math.Max(1, logicalViewport.Height));
            visibleSubmissionCount += work.VisibleSubmissionCount;
            batchCount += work.BatchCount;
        }
        LastRenderStatistics = CompleteStatistics(renderedCameraCount,
            new RenderWorkStatistics(visibleSubmissionCount, batchCount),
            targetWidth, targetHeight, drawStart, hasCompleteDrawStatistics);
    }

    public static GraphicsRect ResolveViewport(
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
        if (backend == GraphicsBackend.OpenGL)
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

    public void FillViewport(GraphicsRect viewport, NVector4 color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        PrepareViewport(viewport);
        _device.SetBlendMode(GraphicsBlendMode.Disabled);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        DrawVertices(_triangleMesh, CreateScreenQuad(0, 0, viewport.Width, viewport.Height, color),
            viewport.Width, viewport.Height);
        _device.SetScissor(null);
    }

    internal void DrawGizmos(IReadOnlyList<GizmoLine2D> gizmos, RenderCamera camera,
        GraphicsRect viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gizmos);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0 || gizmos.Count == 0) return;
        PrepareViewport(viewport);
        var vertices = new List<float>(gizmos.Count * 12);
        foreach (var line in gizmos)
        {
            AddLine(vertices,
                camera.WorldToViewport(line.From, viewport.Width, viewport.Height),
                camera.WorldToViewport(line.To, viewport.Width, viewport.Height),
                Numerics.ToNumerics(line.Color));
        }
        DrawVertices(_lineMesh, vertices, viewport.Width, viewport.Height);
        _device.SetScissor(null);
    }

    private void ClearDepth(GraphicsRect viewport)
    {
        PrepareViewport(viewport);
        _device.SetDepthState(GraphicsDepthState.Default);
        _device.Clear(GraphicsClearFlags.Depth, default);
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetScissor(null);
    }

    private RenderWorkStatistics RenderCore(IReadOnlyList<Scene> scenes, Scene activeScene, RenderCamera camera,
        GraphicsRect viewport, bool clearTarget, bool initializeColor, bool drawGrid, bool drawUi,
        bool drawExtensions, bool drawGizmos,
        int logicalWidth = 0, int logicalHeight = 0, Predicate<GameObject>? objectFilter = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(activeScene);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0) return default;
        var renderWidth = logicalWidth > 0 ? logicalWidth : viewport.Width;
        var renderHeight = logicalHeight > 0 ? logicalHeight : viewport.Height;

        PrepareViewport(viewport);
        if (clearTarget)
            _device.Clear(GraphicsClearFlags.Color, camera.ClearColor);
        else if (initializeColor)
            DrawVertices(_triangleMesh,
                CreateScreenQuad(0, 0, renderWidth, renderHeight, camera.ClearColor),
                renderWidth, renderHeight);

        if (drawGrid) DrawGrid(camera, renderWidth, renderHeight);

        var submissions = Collect(scenes, camera, renderWidth, renderHeight,
            drawUi, drawExtensions, objectFilter);
        var batches = RenderBatchBuilder2D.Build(submissions);
        foreach (var batch in batches)
        {
            if (SceneRenderContributor2DRegistry.TryRender(
                    _device, batch, camera, renderWidth, renderHeight, viewport))
            {
                PrepareViewport(viewport);
                continue;
            }
            DrawBatch(batch, camera, renderWidth, renderHeight);
        }

        if (drawGizmos) DrawCameraGizmos(scenes, camera, renderWidth, renderHeight);
        _device.SetScissor(null);
        return new RenderWorkStatistics(submissions.Count, batches.Count);
    }

    private GraphicsDrawStatistics CaptureDrawStatistics(out bool complete)
    {
        complete = _device is IGraphicsDeviceStatistics;
        return complete
            ? ((IGraphicsDeviceStatistics)_device).DrawStatistics
            : default;
    }

    private SceneRenderStatistics CompleteStatistics(
        int cameraCount,
        RenderWorkStatistics work,
        int targetWidth,
        int targetHeight,
        GraphicsDrawStatistics drawStart,
        bool hasCompleteDrawStatistics)
    {
        var draw = hasCompleteDrawStatistics
            ? ((IGraphicsDeviceStatistics)_device).DrawStatistics - drawStart
            : default;
        return new SceneRenderStatistics(
            cameraCount,
            work.VisibleSubmissionCount,
            work.BatchCount,
            draw.DrawCallCount,
            draw.VertexCount,
            draw.TriangleCount,
            draw.LineCount,
            targetWidth,
            targetHeight,
            hasCompleteDrawStatistics);
    }

    private IReadOnlyList<RenderSubmission2D> Collect(
        IReadOnlyList<Scene> scenes, RenderCamera camera, int width, int height, bool drawUi,
        bool drawExtensions, Predicate<GameObject>? objectFilter)
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
                if (objectFilter is not null && !objectFilter(renderer.gameObject)) continue;
                if (!camera.ContainsLayer(renderer.sortingLayer)) continue;
                var color = WithOpacity(renderer.color, renderer.opacity);
                var visual = renderer.ResolveSpriteUnchecked();
                var pivot = renderer.useSpritePivot && renderer.sprite is not null
                    ? visual.Pivot : renderer.pivot;
                var localCenter = new Vector2(
                    (Fix64.Half - pivot.x) * renderer.size.x,
                    (Fix64.Half - pivot.y) * renderer.size.y);
                var renderedSize = Vector2.Scale(renderer.size, renderer.transform.lossyScale);
                var quad = new Quad(renderer.transform.TransformPoint(localCenter), renderer.transform.rotation,
                    Abs(renderedSize), color,
                    visual.Texture, visual.Uv,
                    renderer.flipX ^ renderedSize.x < 0,
                    renderer.flipY ^ renderedSize.y < 0,
                    visual.IsTextured);
                if (!camera.IsVisible(quad.Center, quad.Rotation, quad.Size, width, height)) continue;
                submissions.Add(new RenderSubmission2D(
                    new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                        hierarchy.GetValueOrDefault(renderer.gameObject), renderer.TransparencyUnchecked(color),
                        sequence++),
                    renderer.BatchKey(visual),
                    quad));
            }
            foreach (var system in scene.QueryComponents<ParticleSystem2D>())
            {
                if (!system.enabled || !system.gameObject.activeInHierarchy) continue;
                if (objectFilter is not null && !objectFilter(system.gameObject)) continue;
                if (!camera.ContainsLayer(system.sortingLayer)) continue;
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
                    var renderedSize = Vector2.Scale(particle.Size, system.transform.lossyScale);
                    var quad = new Quad(worldPosition, rotation,
                        Abs(renderedSize), color,
                        visual.Texture, visual.Uv, renderedSize.x < 0, renderedSize.y < 0,
                        visual.IsTextured);
                    if (!camera.IsVisible(quad.Center, quad.Rotation, quad.Size, width, height)) continue;
                    submissions.Add(new RenderSubmission2D(
                        new RenderSortKey2D(system.sortingLayer, system.orderInLayer, hierarchyOrder,
                            system.TransparencyUnchecked(color), sequence++),
                        system.BatchKey(visual),
                        quad));
                }
            }
            if (drawExtensions)
                sequence = SceneRenderContributor2DRegistry.Collect(
                    _device, scene, camera, width, height, submissions, sequence, drawUi, objectFilter);
        }
        return submissions.Where(submission => camera.ContainsLayer(submission.SortKey.Layer)).ToArray();
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
            var cameraViewport = camera.viewportRect;
            var cameraWidth = Math.Max(1, (int)(width * (float)cameraViewport.width));
            var cameraHeight = Math.Max(1, (int)(height * (float)cameraViewport.height));
            var points = RenderCamera.From(camera).ViewBoundary(cameraWidth, cameraHeight);
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
        _device.SetRasterizerState(GraphicsRasterizerState.CullBackFaces);
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
        var u0 = (float)quad.Uv.x;
        var u1 = (float)quad.Uv.xMax;
        var v0 = (float)quad.Uv.y;
        var v1 = (float)quad.Uv.yMax;
        if (quad.FlipX) (u0, u1) = (u1, u0);
        if (quad.FlipY) (v0, v1) = (v1, v0);
        var half = quad.Size * Fix64.Half;
        var local = new[]
        {
            new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y),
            new Vector2(half.x, half.y), new Vector2(-half.x, half.y)
        };
        var points = local.Select(point => camera.WorldToViewport(
            quad.Center + Transform.RotateVector(point, quad.Rotation), width, height)).ToArray();
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
    private static Vector2 Abs(Vector2 value) =>
        new(Fix64.Abs(value.x), Fix64.Abs(value.y));

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

    private readonly record struct RenderWorkStatistics(
        int VisibleSubmissionCount,
        int BatchCount);

}
