using Quad = BEngine.Rendering.SceneQuad2D;
using QuadPayload = BEngine.Rendering.SceneQuadPayload2D;
using BEngine.Rendering.Rhi;
using BEngine.Platform;
using System.Runtime.InteropServices;
using System.Diagnostics;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed partial class PortableSceneRenderer : ISceneRenderer
{
    private static readonly Vector2 CoarseCullPadding = Vector2.one * Fix64.Parse("0.0001");
    private readonly BoundedLruCache<SliceKey, SpriteSlice[]> _sliceGeometry = new();
    private readonly record struct SliceKey(SpriteDrawMode Mode, Vector2 Size, Vector2 Pivot, Vector2 Pixels,
        Vector4 Border, Fix64 PixelsPerUnit, Rect Uv, bool FlipX, bool FlipY);
    private static readonly GraphicsVertexLayout VertexLayout = new(
        6 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float))
        ]);
    public static readonly GraphicsVertexLayout TexturedVertexLayout = new(
        8 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
        ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsDebugAnnotations? _debugAnnotations;
    private readonly bool _ownsDevice;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _triangleMesh;
    private readonly IGraphicsMesh _lineMesh;
    private readonly IGraphicsMesh _utilityMesh;
    private readonly GraphicsMeshUploadCache _utilityUploads;
    private readonly SceneTextureCache _textures;
    private readonly List<RenderSubmission2D> _submissions = [];
    private readonly List<QuadPayload> _quadPayloads = [];
    private readonly FrameDataBuffer<Quad> _quadData = new();
    private static readonly object CoreQuadPayload = new();
    private int _quadPayloadCount;
    private int _quadPayloadUsedCount;
    private readonly List<ISceneRenderContributor2D> _frameContributors = [];
    private readonly List<QuadProcessor> _processQuadContributors = [];
    private bool _contributorsPrepared;
    private readonly List<ISceneRenderContributor2D?> _batchContributors = [];
    private readonly List<CoreBatchRange> _coreBatchRanges = [];
    private readonly List<RenderBatch2D> _batches = [];
    private static readonly IReadOnlyDictionary<GameObject, long> EmptyHierarchy = new Dictionary<GameObject, long>();
    private IReadOnlyDictionary<GameObject, long> _hierarchyOrder = EmptyHierarchy;
    private readonly List<SpriteSlice> _spriteSlices = [];
    private readonly List<CameraRenderItem> _orderedCameras = [];
    private readonly List<float> _triangleVertices = [];
    private readonly List<float> _texturedVertices = [];
    private readonly List<float> _lineVertices = [];
    private IGraphicsProgram? _texturedProgram;
    private IGraphicsMesh? _texturedMesh;
    private readonly GraphicsMeshUploadCache _triangleUploads;
    private readonly GraphicsMeshUploadCache _lineUploads;
    private GraphicsMeshUploadCache? _texturedUploads;
    private double _collectMilliseconds, _sortMilliseconds, _geometryMilliseconds, _submitMilliseconds;
    private double _coreCollectMilliseconds, _extensionCollectMilliseconds;
    private long _geometryUploadCount, _geometryUploadBytes;
    private IGraphicsRenderTarget? _postProcessTarget;
    private int _rendering;
    private SceneRenderDiagnostics.CaptureContext? _renderDiagnostics;
    private bool _disposed;

    public GraphicsBackend Backend => _device.Backend;
    public GraphicsDeviceCapabilities Capabilities => _device.Capabilities;
    public SceneRenderStatistics LastRenderStatistics { get; private set; }

    public BTask<bool> PrepareTextureAsync(string source, AsyncLoadContext? context = null) =>
        _textures.PrepareAsync(source, context);

    public PortableSceneRenderer(
        IGraphicsDevice device,
        bool ownsDevice = false,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _debugAnnotations = device as IGraphicsDebugAnnotations;
        _ownsDevice = ownsDevice;
        _geometrySink = new GeometrySink(this);
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
        _utilityMesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.Scene2D.Utility", ReadOnlyMemory<float>.Empty, VertexLayout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
        _utilityUploads = new GraphicsMeshUploadCache(_utilityMesh);
        _triangleUploads = new GraphicsMeshUploadCache(_triangleMesh);
        _lineUploads = new GraphicsMeshUploadCache(_lineMesh);
        _textures = new SceneTextureCache(device, fileSystem, pathService, platformService);
    }

    public void Render(Scene scene, RenderCamera camera, int width, int height,
        bool drawGrid = false, bool drawUi = true, bool drawExtensions = true, bool drawGizmos = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rendering != 0) throw new InvalidOperationException("PortableSceneRenderer does not support reentrant rendering.");
        _singleScene[0] = scene;
        try { Render(_singleScene, scene, camera, width, height, drawGrid, drawUi, drawExtensions, drawGizmos); }
        finally { _singleScene[0] = null!; }
    }

    public void Render(IReadOnlyList<Scene> scenes, Scene activeScene, RenderCamera camera, int width, int height,
        bool drawGrid = false, bool drawUi = true, bool drawExtensions = true, bool drawGizmos = false)
    {
        using var renderScope = EnterRender();
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
        using var renderScope = EnterRender();
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
        using var renderScope = EnterRender();
        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);
        // The render scale only affects post-process intermediate targets. The
        // final composite still covers the requested physical viewport, so
        // callers retain their layout and presentation dimensions.
        var renderScale = Fix64.One;
        if (RenderSettingsUtility.TryGetCurrent(out var renderSettings))
            renderScale = renderSettings!.renderScale;
        var drawStart = CaptureDrawStatistics(out var hasCompleteDrawStatistics);
        var renderedCameraCount = 0;
        var visibleSubmissionCount = 0;
        var batchCount = 0;

        if (targetViewport.X == 0 && targetViewport.Y == 0 &&
            targetViewport.Width == targetWidth && targetViewport.Height == targetHeight)
            ClearWholeTarget(targetViewport, RenderCamera.Default.ClearColor, clearDepth: false);
        else FillViewport(targetViewport, RenderCamera.Default.ClearColor);
        _orderedCameras.Clear();
        for (var sequence = 0; sequence < cameras.Count; sequence++)
        {
            var current = cameras[sequence];
            if (current.enabled && current.gameObject.activeInHierarchy)
                _orderedCameras.Add(new CameraRenderItem(current, sequence));
        }
        if (_orderedCameras.Count > 1)
            _orderedCameras.Sort(static (left, right) =>
            {
                var priority = left.Camera.priority.CompareTo(right.Camera.priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });
        foreach (var item in _orderedCameras)
        {
            var camera = RenderCamera.From(item.Camera);
            var cameraScene = item.Camera.gameObject.scene ?? activeScene;
            var renderTexture = item.Camera.targetTexture;
            var renderTarget = renderTexture?.GetOrCreateTarget(_device);
            using var targetScope = GraphicsRenderTargetScope.Push(_device, renderTarget);
            RenderTexture.active = renderTexture;
            var destination = renderTarget is null
                ? targetViewport
                : new GraphicsRect(0, 0, renderTarget.Width, renderTarget.Height);
            var destinationWidth = renderTarget?.Width ?? targetWidth;
            var destinationHeight = renderTarget?.Height ?? targetHeight;
            var viewport = ResolveViewport(destination, camera.ViewportRect, _device.Backend);
            if (viewport.Width <= 0 || viewport.Height <= 0)
            {
                if (ReferenceEquals(RenderTexture.active, renderTexture)) RenderTexture.active = null;
                continue;
            }
            renderedCameraCount++;
            var logicalViewport = ResolveViewport(
                new GraphicsRect(0, 0, destinationWidth, destinationHeight),
                camera.ViewportRect, _device.Backend);
            var markedCamera = _debugAnnotations?.DebugMarkersEnabled == true;
            if (markedCamera)
                _debugAnnotations!.PushDebugMarker(new GraphicsDebugMarker(
                    Group: $"Camera: {item.Camera.gameObject.name}",
                    SourceName: $"{item.Camera.gameObject.name} (Camera2D)",
                    SourceInstanceId: item.Camera.GetInstanceID()));
            try
            {
                if (ScenePostProcessor2DRegistry.HasActive(_device, cameraScene, item.Camera))
                {
                    var intermediateWidth = ScaleRenderDimension(logicalViewport.Width, renderScale);
                    var intermediateHeight = ScaleRenderDimension(logicalViewport.Height, renderScale);
                    var intermediate = GetPostProcessTarget(
                        intermediateWidth, intermediateHeight);
                    using (GraphicsRenderTargetScope.Push(_device, intermediate))
                    {
                        var intermediateViewport = new GraphicsRect(
                            0, 0, intermediate.Width, intermediate.Height);
                        ClearWholeTarget(intermediateViewport, camera.ClearMode == CameraClearMode.Color
                            ? camera.ClearColor
                            : default, clearDepth: true);
                        var intermediateWork = RenderCore(scenes, activeScene, camera, intermediateViewport,
                            clearTarget: false, initializeColor: false, drawGrid: false, drawUi,
                            drawExtensions: true, drawGizmos: false,
                            logicalWidth: intermediate.Width, logicalHeight: intermediate.Height);
                        visibleSubmissionCount += intermediateWork.VisibleSubmissionCount;
                        batchCount += intermediateWork.BatchCount;
                    }
                    if (intermediate.ColorTexture is not null)
                        ScenePostProcessor2DRegistry.Render(
                            _device, cameraScene, item.Camera, intermediate.ColorTexture, viewport);
                    continue;
                }

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
            finally
            {
                if (markedCamera) _debugAnnotations!.PopDebugMarker();
                if (ReferenceEquals(RenderTexture.active, renderTexture)) RenderTexture.active = null;
            }
        }
        LastRenderStatistics = CompleteStatistics(renderedCameraCount,
            new RenderWorkStatistics(visibleSubmissionCount, batchCount),
            targetWidth, targetHeight, drawStart, hasCompleteDrawStatistics);
    }

    private IGraphicsRenderTarget GetPostProcessTarget(int width, int height)
    {
        _device.Capabilities.Require(GraphicsDeviceFeatures.RenderTargets |
                                     GraphicsDeviceFeatures.SampledTextures);
        if (_postProcessTarget is not null && _postProcessTarget.Width == width &&
            _postProcessTarget.Height == height) return _postProcessTarget;
        _postProcessTarget?.Dispose();
        _postProcessTarget = _device.CreateRenderTarget("BEngine.Scene2D.PostProcessSource",
            new GraphicsRenderTargetDescription(width, height,
                GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureFormat.Depth24Stencil8,
                SampleColor: true, Filter: GraphicsTextureFilter.Linear));
        return _postProcessTarget;
    }

    private static int ScaleRenderDimension(int dimension, Fix64 scale)
    {
        if (scale >= Fix64.One) return Math.Max(1, dimension);
        return Math.Max(1, (int)Math.Round(Math.Max(1, dimension) * (double)scale,
            MidpointRounding.AwayFromZero));
    }

    public static GraphicsRect ResolveViewport(
        GraphicsRect target, Rect normalizedViewport, GraphicsBackend backend) =>
        GraphicsUtility.Current.ResolveViewport(target, normalizedViewport, backend);

    public void FillViewport(GraphicsRect viewport, NVector4 color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        PrepareViewport(viewport);
        _device.SetBlendMode(GraphicsBlendMode.Disabled);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        FillScreenQuad(_triangleVertices, 0, 0, viewport.Width, viewport.Height, color);
        DrawVertices(_utilityMesh, _triangleVertices, viewport.Width, viewport.Height);
        _device.SetScissor(null);
    }

    public void DrawGizmos(IReadOnlyList<GizmoLine2D> gizmos, RenderCamera camera,
        GraphicsRect viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gizmos);
        viewport.Validate();
        if (viewport.Width <= 0 || viewport.Height <= 0 || gizmos.Count == 0) return;
        PrepareViewport(viewport);
        _triangleVertices.Clear();
        _triangleVertices.EnsureCapacity(gizmos.Count * 36);
        foreach (var line in gizmos)
        {
            AddThickLine(_triangleVertices,
                camera.WorldToViewport(line.From, viewport.Width, viewport.Height),
                camera.WorldToViewport(line.To, viewport.Width, viewport.Height),
                Numerics.ToNumerics(line.Color), (float)line.LineWidth);
        }
        DrawVertices(_utilityMesh, _triangleVertices, viewport.Width, viewport.Height);
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

    private void ClearWholeTarget(GraphicsRect viewport, NVector4 color, bool clearDepth)
    {
        // Native clears can ignore the viewport on some backends, so callers
        // only use this when the viewport covers the whole active attachment.
        // Partial camera viewports continue using FillViewport's geometry.
        PrepareViewport(viewport);
        if (clearDepth) _device.SetDepthState(GraphicsDepthState.Default);
        _device.Clear(clearDepth ? GraphicsClearFlags.Color | GraphicsClearFlags.Depth : GraphicsClearFlags.Color, color);
        if (clearDepth) _device.SetDepthState(GraphicsDepthState.Disabled);
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
        camera = camera.ForViewport(renderWidth, renderHeight);

        PrepareViewport(viewport);
        if (clearTarget)
            _device.Clear(GraphicsClearFlags.Color, camera.ClearColor);
        else if (initializeColor)
        {
            FillScreenQuad(_triangleVertices, 0, 0, renderWidth, renderHeight, camera.ClearColor);
            DrawVertices(_utilityMesh, _triangleVertices, renderWidth, renderHeight);
        }

        if (drawGrid) DrawGrid(camera, renderWidth, renderHeight);

        var stageStarted = Stopwatch.GetTimestamp();
        var submissions = Collect(scenes, camera, renderWidth, renderHeight,
            drawUi, drawExtensions, objectFilter);
        _collectMilliseconds += Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
        stageStarted = Stopwatch.GetTimestamp();
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.Sort))
        { if (_submissionSort.Sort(submissions)) _reusedSorts++; else _fullSorts++; }
        int batchCount;
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.BuildContiguous))
        { batchCount = RenderBatchBuilder2D.BuildContiguous(submissions, _batches); }
        _sortMilliseconds += Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
        try
        {
            stageStarted = Stopwatch.GetTimestamp();
            PrepareCachedBatchGeometry(batchCount, camera, renderWidth, renderHeight);
            _geometryMilliseconds += Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
            stageStarted = Stopwatch.GetTimestamp();
            for (var index = 0; index < batchCount; index++)
            {
                if (TryDrawMergedCoreRanges(ref index, batchCount, renderWidth, renderHeight)) continue;
                var batch = _batches[index];
                using var gpuPass = GraphicsFrameTimingService.IsPassTimingEnabled
                    ? (_device as IGraphicsPassTimingSource)?.BeginGpuPass(batch.BatchKey.Shader + " · " + batch.Submissions.Count)
                    : null;
                var markedBatch = _debugAnnotations?.DebugMarkersEnabled == true;
                if (markedBatch)
                    _debugAnnotations!.PushDebugMarker(new GraphicsDebugMarker(
                        BatchName: $"{batch.BatchKey.Shader} ({batch.Submissions.Count} submissions)",
                        Material: batch.BatchKey.Material == Guid.Empty ? null : batch.BatchKey.Material,
                        Shader: batch.BatchKey.Shader,
                        Atlas: batch.BatchKey.Atlas));
                try
                {
                    if (batch.BatchKey.MaskGroup > 0)
                    {
                        DrawMaskedBatch(index, batch, camera, renderWidth, renderHeight, viewport);
                        continue;
                    }
                    if (_batchContributors[index] is { } contributor)
                    {
                        using (SceneRenderDiagnostics.MeasureRenderer(contributor))
                            contributor.Render(batch, camera, renderWidth, renderHeight, viewport);
                        PrepareViewport(viewport);
                        continue;
                    }
                    DrawPreparedBatch(_coreBatchRanges[index], renderWidth, renderHeight);
                }
                finally
                {
                    if (markedBatch) _debugAnnotations!.PopDebugMarker();
                }
            }
            _submitMilliseconds += Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
        }
        finally
        {
            for (var index = 0; index < batchCount; index++) _batches[index].Clear();
        }

        if (drawGizmos) DrawCameraGizmos(scenes, camera, renderWidth, renderHeight);
        _device.SetScissor(null);
        return new RenderWorkStatistics(submissions.Count, batchCount);
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
            hasCompleteDrawStatistics)
        {
            CollectMilliseconds = _collectMilliseconds,
            RenderBreakdown = SceneRenderDiagnostics.CaptureStatistics(cameraCount),
            CoreCollectMilliseconds = _coreCollectMilliseconds,
            ExtensionCollectMilliseconds = _extensionCollectMilliseconds,
            SortMilliseconds = _sortMilliseconds,
            GeometryMilliseconds = _geometryMilliseconds,
            SubmitMilliseconds = _submitMilliseconds,
            CoreGeometryUploadCount = _geometryUploadCount,
            CoreGeometryUploadBytes = _geometryUploadBytes,
            CoreGeometryBuiltQuadCount = _geometryBuiltQuads,
            CoreGeometryReusedQuadCount = _geometryReusedQuads,
            CoreSpriteWorldGeometryBuiltCount = _worldBuilt,
            CoreSpriteWorldGeometryReusedCount = _worldReused,
            CoreSpriteCullingTestCount = _cullTests,
            CoreSpriteCullingReuseCount = _cullReused,
            CoreSpriteCulledCount = _culledSprites,
            SortFullPassCount = _fullSorts,
            SortReusePassCount = _reusedSorts,
            CoreDrawRangeMergeCount = _mergedCoreRanges,
            CoreOpaqueSolidDrawCount = _opaqueSolidDraws
        };
    }

    private List<RenderSubmission2D> Collect(
        IReadOnlyList<Scene> scenes, RenderCamera camera, int width, int height, bool drawUi,
        bool drawExtensions, Predicate<GameObject>? objectFilter)
    {
        _submissions.Clear();
        ResetMaskGroups(); _maskLookup.Clear(); _paintedMaskGroup = 0;
        _quadPayloadCount = 0;
        _quadData.Clear();
        if (!_contributorsPrepared)
        {
            _contributorsPrepared = true;
            if (drawExtensions)
                foreach (var contributor in SceneRenderContributor2DRegistry.GetContributors(_device))
                    if (RuntimePackageState.IsEnabled(contributor.packageId))
                    {
                        _frameContributors.Add(contributor);
                        contributor.BeginFrame();
                        if (contributor.processesQuads) _processQuadContributors.Add(new QuadProcessor(contributor));
                    }
        }
        PrepareCulling(camera, width, height);
        var culling = _culling;
        var spriteSlot = 0;
        long sequence = 0;
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            if (scene is null || !scene.isLoaded) continue;
            _hierarchyOrder = HierarchyOrder2D.Get(scene);
            var context = new SceneRenderContext2D(scene, camera, width, height, _hierarchyOrder, objectFilter);
            var masks = scene.ComponentsSpanUnchecked<SpriteMask>();
            var extensionStarted = Stopwatch.GetTimestamp();
            foreach (var contributor in _frameContributors)
                using (SceneRenderDiagnostics.MeasureRenderer(contributor))
                    contributor.BeginScene(in context);
            _extensionCollectMilliseconds += Stopwatch.GetElapsedTime(extensionStarted).TotalMilliseconds;
            var coreStarted = Stopwatch.GetTimestamp();
            using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.SimpleSprites))
            {
            foreach (var renderer in scene.ComponentsSpanUnchecked<SpriteRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (objectFilter is not null && !objectFilter(renderer.gameObject)) continue;
                if (!camera.ContainsLayer(renderer.sortingLayer)) continue;
                var color = WithOpacity(renderer.color, renderer.opacity);
                var visual = renderer.ResolveSpriteUnchecked();
                var pivot = renderer.useSpritePivot && renderer.sprite is not null
                    ? visual.Pivot : renderer.pivot;
                if (renderer.drawMode != SpriteDrawMode.Simple)
                {
                    var sliceSize = Abs(renderer.size);
                    // A custom camera policy can move each slice independently; only coarse-cull
                    // an unsnapped sprite, with padding for fixed-point rounding at its edges.
                    var coarse = Quad.Create(new Vector2((Fix64.Half - pivot.x) * sliceSize.x, (Fix64.Half - pivot.y) * sliceSize.y),
                        0, sliceSize + CoarseCullPadding, color, visual.Texture, visual.Uv, false, false, visual.IsTextured)
                        .WithTransform(renderer.transform.localToWorldMatrix);
                    if (camera.Policy is null && !culling.IsVisible(coarse)) continue;
                    var maskGroup = GetMaskGroup(renderer, masks);
                    if (maskGroup < 0) continue;
                    var sliceKey = new SliceKey(renderer.drawMode, renderer.size, pivot, visual.PixelSize,
                        renderer.sprite?.border ?? Vector4.zero, renderer.sprite?.pixelsPerUnit ?? 100,
                        visual.Uv, renderer.flipX ^ renderer.size.x < 0, renderer.flipY ^ renderer.size.y < 0);
                    if (!_sliceGeometry.TryGetValue(sliceKey, out var slices))
                    {
                        SpriteSliceGeometry.Build(sliceKey.Mode, sliceKey.Size, sliceKey.Pivot, sliceKey.Pixels,
                            sliceKey.Border, sliceKey.PixelsPerUnit, sliceKey.Uv, sliceKey.FlipX, sliceKey.FlipY, _spriteSlices);
                        slices = _spriteSlices.ToArray();
                        _sliceGeometry.Add(sliceKey, slices, slices.Length * 64L);
                        _sliceGeometry.Trim(128, 8 * 1024 * 1024);
                    }
                    renderer.transform.GetWorldPoseUnchecked(out _, out var rotation, out var scale);
                    foreach (var slice in slices)
                    {
                        var sliced = CreateQuad(default, rotation,
                            Abs(Vector2.Scale(slice.Size, scale)), color, visual.Texture, slice.Uv,
                            renderer.flipX ^ renderer.size.x * scale.x < 0,
                            renderer.flipY ^ renderer.size.y * scale.y < 0, visual.IsTextured);
                        sliced = WithSpriteAxes(sliced, renderer.transform, slice.Center, slice.Size, camera);
                        if (!culling.IsVisible(sliced)) continue;
                        if (renderer.receiveLighting) ApplySurfaceEffectsReferenced(ref sliced, renderer.gameObject);
                        _submissions.Add(new RenderSubmission2D(new RenderSortKey2D(renderer.sortingLayer,
                            renderer.orderInLayer, _hierarchyOrder.GetValueOrDefault(renderer.gameObject),
                            renderer.TransparencyUnchecked(color), sequence++), renderer.BatchKey(visual) with { MaskGroup = maskGroup }, ReusePayload(sliced))
                            { SceneQuadIndex = _quadPayloadCount });
                    }
                    continue;
                }
                if (!TryCollectSimpleSprite(renderer, visual, pivot, color, camera, spriteSlot++, out var quad)) continue;
                var simpleMaskGroup = GetMaskGroup(renderer, masks);
                if (simpleMaskGroup < 0) continue;
                if (renderer.receiveLighting)
                    ApplySurfaceEffectsReferenced(ref quad, renderer.gameObject);
                _submissions.Add(new RenderSubmission2D(
                    new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                        _hierarchyOrder.GetValueOrDefault(renderer.gameObject), renderer.TransparencyUnchecked(color),
                        sequence++),
                    renderer.BatchKey(visual) with { MaskGroup = simpleMaskGroup },
                    ReusePayload(quad)) { SceneQuadIndex = _quadPayloadCount });
            }
            }
            var sink = _geometrySink;
            sink.Reset(camera, width, height, sequence);
            // Preserve public filter callback order/count when a filter is supplied.
            var geometryRenderers = objectFilter is null
                ? scene.NonSpriteRenderersSpanUnchecked()
                : scene.ComponentsSpanUnchecked<Renderer2D>();
            using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.NonSpriteRenderers))
            {
            foreach (var renderer in geometryRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (objectFilter is not null && !objectFilter(renderer.gameObject)) continue;
                if (!camera.ContainsLayer(renderer.sortingLayer)) continue;
                using (SceneRenderDiagnostics.MeasureRenderer(renderer))
                { renderer.CollectGeometry(sink); }
            }
            }
            sequence = sink.Sequence;
            _coreCollectMilliseconds += Stopwatch.GetElapsedTime(coreStarted).TotalMilliseconds;
            extensionStarted = Stopwatch.GetTimestamp();
            if (drawExtensions)
                foreach (var contributor in _frameContributors)
                    if (drawUi || !contributor.rendersUi)
                        using (SceneRenderDiagnostics.MeasureRenderer(contributor))
                            sequence = contributor.Collect(in context, _submissions, sequence);
            _extensionCollectMilliseconds += Stopwatch.GetElapsedTime(extensionStarted).TotalMilliseconds;
        }
        var visibleCount = 0;
        var submitted = CollectionsMarshal.AsSpan(_submissions);
        for (var index = 0; index < submitted.Length; index++)
        {
            ref readonly var submission = ref submitted[index];
            if (!camera.ContainsLayer(submission.SortKey.Layer)) continue;
            if (visibleCount != index) submitted[visibleCount] = submission;
            visibleCount++;
        }
        if (visibleCount < _submissions.Count)
            _submissions.RemoveRange(visibleCount, _submissions.Count - visibleCount);
        return _submissions;
    }

    private void DrawPreparedBatch(CoreBatchRange range, int width, int height)
    {
        if (range.VertexCount == 0) return;
        var program = range.Textured ? _texturedProgram! : _program;
        program.Bind();
        program.SetFloat("uViewportWidth", width); program.SetFloat("uViewportHeight", height);
        if (range.Textured)
        {
            program.SetInt("uTexture", 0);
            _device.BindTexture(0, _textures.Resolve(range.Texture!));
        }
        if (range.Opaque) _device.SetBlendMode(GraphicsBlendMode.Disabled);
        try
        {
            _device.Draw(range.Textured ? _texturedMesh! : _triangleMesh, range.VertexCount, range.FirstVertex);
            if (range.Opaque) _opaqueSolidDraws++;
        }
        finally
        {
            // Contributors and mask composition observe the same incoming state
            // as before this optimization; only this known solid draw overrides it.
            if (range.Opaque) _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        }
    }

    private void Upload(GraphicsMeshUploadCache cache, List<float> vertices, bool rebuiltProjection = false)
    {
        if (rebuiltProjection) cache.UpdateRebuilt(CollectionsMarshal.AsSpan(vertices));
        else if (!cache.Update(CollectionsMarshal.AsSpan(vertices))) return;
        _geometryUploadCount++; _geometryUploadBytes += vertices.Count * sizeof(float);
    }

    private void DrawGrid(RenderCamera camera, int width, int height)
    {
        _lineVertices.Clear();
        var halfWidth = camera.Size * width / Math.Max(1, height);
        var left = (int)(camera.Position.x - halfWidth) - 1;
        var right = (int)(camera.Position.x + halfWidth) + 1;
        var bottom = (int)(camera.Position.y - camera.Size) - 1;
        var top = (int)(camera.Position.y + camera.Size) + 1;
        var minor = new NVector4(0.27f, 0.29f, 0.32f, 0.65f);
        var axis = new NVector4(0.42f, 0.44f, 0.48f, 0.9f);
        for (var x = left; x <= right; x++)
            AddLine(_lineVertices, camera.WorldToViewport(new Vector2(x, bottom), width, height),
                camera.WorldToViewport(new Vector2(x, top), width, height), x == 0 ? axis : minor);
        for (var y = bottom; y <= top; y++)
            AddLine(_lineVertices, camera.WorldToViewport(new Vector2(left, y), width, height),
                camera.WorldToViewport(new Vector2(right, y), width, height), y == 0 ? axis : minor);
        if (_lineVertices.Count > 0) DrawVertices(_lineMesh, _lineVertices, width, height);
    }

    private void DrawCameraGizmos(IReadOnlyList<Scene> scenes, RenderCamera view, int width, int height)
    {
        _lineVertices.Clear();
        foreach (var scene in scenes)
        {
            foreach (var camera in scene.ComponentsSpanUnchecked<Camera2D>())
            {
                if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
                var cameraViewport = camera.viewportRect;
                var cameraWidth = Math.Max(1, (int)(width * (float)cameraViewport.width));
                var cameraHeight = Math.Max(1, (int)(height * (float)cameraViewport.height));
                var points = RenderCamera.From(camera).ViewBoundary(cameraWidth, cameraHeight);
                for (var index = 0; index < points.Length; index++)
                    AddLine(_lineVertices, view.WorldToViewport(points[index], width, height),
                        view.WorldToViewport(points[(index + 1) % points.Length], width, height),
                        new NVector4(1f, 0.72f, 0.12f, 1f));
            }
        }
        if (_lineVertices.Count > 0) DrawVertices(_lineMesh, _lineVertices, width, height);
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
        Upload(ReferenceEquals(mesh, _triangleMesh) ? _triangleUploads : ReferenceEquals(mesh, _utilityMesh) ? _utilityUploads : _lineUploads, vertices);
        _program.Bind();
        _program.SetFloat("uViewportWidth", width);
        _program.SetFloat("uViewportHeight", height);
        _device.Draw(mesh);
    }

    private static void AddQuad(List<float> output, Quad quad, RenderViewportTransform2D projection) =>
        WriteQuadAt(output, output.Count, quad, projection);

    private static void WriteQuadAt(List<float> output, int offset, Quad quad, RenderViewportTransform2D projection)
    {
        Span<NVector2> corners = stackalloc NVector2[4];
        projection.FillQuadCorners(corners, quad);
        var a = corners[0]; var b = corners[1]; var c = corners[2]; var d = corners[3];
        var end = checked(offset + 36);
        if (output.Count < end) CollectionsMarshal.SetCount(output, end);
        var vertices = MemoryMarshal.Cast<float, SolidQuadVertex>(CollectionsMarshal.AsSpan(output).Slice(offset, 36));
        var colorA = Numerics.ToNumerics(quad.ColorA); var colorB = Numerics.ToNumerics(quad.ColorB);
        var colorC = Numerics.ToNumerics(quad.ColorC); var colorD = Numerics.ToNumerics(quad.ColorD);
        vertices[0] = new(a, colorA); vertices[1] = new(b, colorB); vertices[2] = new(c, colorC);
        vertices[3] = new(a, colorA); vertices[4] = new(c, colorC); vertices[5] = new(d, colorD);
    }

    public static void AddTexturedQuad(
        List<float> output, Quad quad, RenderCamera camera, int width, int height) =>
        AddTexturedQuad(output, quad, camera.CreateViewportTransform(width, height));

    public static void AddTexturedQuad(List<float> output, Quad quad, RenderViewportTransform2D projection) =>
        WriteTexturedQuadAt(output, output.Count, quad, projection);

    /// <summary>Appends a quad using borrowed stable inputs during a synchronous render callback.</summary>
    public static void AddTexturedQuadReferenced(List<float> output, in Quad quad, in RenderViewportTransform2D projection) =>
        WriteTexturedQuadAt(output, output.Count, in quad, in projection);

    private static void WriteTexturedQuadAt(List<float> output, int offset, in Quad quad, in RenderViewportTransform2D projection)
    {
        var u0 = (float)quad.Uv.x;
        var u1 = (float)quad.Uv.xMax;
        var v0 = (float)quad.Uv.y;
        var v1 = (float)quad.Uv.yMax;
        if (quad.FlipX) (u0, u1) = (u1, u0);
        if (quad.FlipY) (v0, v1) = (v1, v0);
        Span<NVector2> corners = stackalloc NVector2[4];
        projection.FillQuadCornersReferenced(corners, in quad);
        var a = corners[0]; var b = corners[1]; var c = corners[2]; var d = corners[3];
        var uvA = new NVector2(u0, v1);
        var uvB = new NVector2(u1, v1);
        var uvC = new NVector2(u1, v0);
        var uvD = new NVector2(u0, v0);
        var end = checked(offset + 48);
        if (output.Count < end) CollectionsMarshal.SetCount(output, end);
        var vertices = MemoryMarshal.Cast<float, TexturedQuadVertex>(CollectionsMarshal.AsSpan(output).Slice(offset, 48));
        var colorA = Numerics.ToNumerics(quad.ColorA); var colorB = Numerics.ToNumerics(quad.ColorB);
        var colorC = Numerics.ToNumerics(quad.ColorC); var colorD = Numerics.ToNumerics(quad.ColorD);
        vertices[0] = new(a, colorA, uvA); vertices[1] = new(b, colorB, uvB); vertices[2] = new(c, colorC, uvC);
        vertices[3] = new(a, colorA, uvA); vertices[4] = new(c, colorC, uvC); vertices[5] = new(d, colorD, uvD);
    }

    // Match the RHI's tightly packed float layouts. Both append callers and the
    // retained core streams write the same quad directly into its final span.
    [StructLayout(LayoutKind.Sequential, Pack = sizeof(float))]
    private readonly struct SolidQuadVertex(NVector2 position, NVector4 color)
    {
        private readonly NVector2 _position = position;
        private readonly NVector4 _color = color;
    }

    [StructLayout(LayoutKind.Sequential, Pack = sizeof(float))]
    private readonly struct TexturedQuadVertex(NVector2 position, NVector4 color, NVector2 uv)
    {
        private readonly NVector2 _position = position;
        private readonly NVector4 _color = color;
        private readonly NVector2 _uv = uv;
    }

    private static void FillScreenQuad(
        List<float> output,
        float x,
        float y,
        float width,
        float height,
        NVector4 color)
    {
        output.Clear();
        output.EnsureCapacity(36);
        var a = new NVector2(x, y);
        var b = new NVector2(x + width, y);
        var c = new NVector2(x + width, y + height);
        var d = new NVector2(x, y + height);
        AddTriangle(output, a, b, c, color);
        AddTriangle(output, a, c, d, color);
    }

    private static void AddTriangle(List<float> output, NVector2 a, NVector2 b, NVector2 c, NVector4 color)
    {
        AddVertex(output, a, color);
        AddVertex(output, b, color);
        AddVertex(output, c, color);
    }
    private static void AddTriangle(List<float> output, NVector2 a, NVector2 b, NVector2 c,
        NVector4 colorA, NVector4 colorB, NVector4 colorC)
    {
        AddVertex(output, a, colorA);
        AddVertex(output, b, colorB);
        AddVertex(output, c, colorC);
    }
    private static void AddLine(List<float> output, NVector2 a, NVector2 b, NVector4 color)
    {
        AddVertex(output, a, color);
        AddVertex(output, b, color);
    }
    private static void AddThickLine(
        List<float> output,
        NVector2 a,
        NVector2 b,
        NVector4 color,
        float width)
    {
        var delta = b - a;
        var length = delta.Length();
        if (!float.IsFinite(length) || length <= 0.0001f) return;
        var halfWidth = Math.Max(1f, width) * 0.5f;
        var normal = new NVector2(-delta.Y, delta.X) * (halfWidth / length);
        AddTriangle(output, a - normal, a + normal, b + normal, color);
        AddTriangle(output, a - normal, b + normal, b - normal, color);
    }
    private static void AddTexturedTriangle(List<float> output,
        NVector2 a, NVector2 b, NVector2 c,
        NVector2 uvA, NVector2 uvB, NVector2 uvC, NVector4 color)
    {
        AddTexturedVertex(output, a, uvA, color);
        AddTexturedVertex(output, b, uvB, color);
        AddTexturedVertex(output, c, uvC, color);
    }
    private static void AddTexturedTriangle(List<float> output,
        NVector2 a, NVector2 b, NVector2 c,
        NVector2 uvA, NVector2 uvB, NVector2 uvC,
        NVector4 colorA, NVector4 colorB, NVector4 colorC)
    {
        AddTexturedVertex(output, a, uvA, colorA);
        AddTexturedVertex(output, b, uvB, colorB);
        AddTexturedVertex(output, c, uvC, colorC);
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
        var pair = GraphicsUtility.Current.ResolveShaderVariant(device.Capabilities, "Shaders/PortableScene/Solid");
        return (pair.Vertex, pair.Fragment);
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
        var pair = GraphicsUtility.Current.ResolveShaderVariant(device.Capabilities, "Shaders/PortableScene/Textured");
        return (pair.Vertex, pair.Fragment);
    }

    private RenderScope EnterRender()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rendering != 0)
            throw new InvalidOperationException(
                "PortableSceneRenderer does not support concurrent or reentrant rendering.");
        _textures.PruneDestroyedRuntimeTextures();
        var diagnostic = SceneRenderDiagnostics.BeginCapture(ref _renderDiagnostics);
        _rendering = 1;
        _contributorsPrepared = false;
        _collectMilliseconds = _sortMilliseconds = _geometryMilliseconds = _submitMilliseconds = 0;
        _coreCollectMilliseconds = _extensionCollectMilliseconds = 0;
        _geometryUploadCount = _geometryUploadBytes = 0;
        _geometryBuiltQuads = _geometryReusedQuads = 0;
        _quadPayloadUsedCount = 0;
        _worldBuilt = _worldReused = _cullTests = _cullReused = _culledSprites = _fullSorts = _reusedSorts = 0;
        _mergedCoreRanges = 0;
        _opaqueSolidDraws = 0;
        return new RenderScope(this, diagnostic);
    }

    private void ExitRender()
    {
        List<Exception>? failures = null;
        try
        {
            foreach (var contributor in _frameContributors)
                try { contributor.EndFrame(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        finally
        {
            _frameContributors.Clear(); _processQuadContributors.Clear();
            _batchContributors.Clear(); _coreBatchRanges.Clear();
            _contributorsPrepared = false;
            for (var index = 0; index < _quadPayloadUsedCount; index++) _quadPayloads[index].Unbind();
            _quadData.Clear();
            ResetMaskGroups(); _maskLookup.Clear();
            _geometrySink.Reset(default, 0, 0, 0);
            _culling.Reset(default, 1, 1);
            _submissions.Clear(); _hierarchyOrder = EmptyHierarchy;
            _orderedCameras.Clear();
            // RenderCore releases active ranges in its finally block. Do not rescan unused pooled batches.
            _rendering = 0;
        }
        if (failures is not null) throw new AggregateException("Render extension frame cleanup failed.", failures);
    }

    public void Dispose()
    {
        if (_disposed) return;
        SceneRenderContributor2DRegistry.Release(_device);
        ScenePostProcessor2DRegistry.Release(_device);
        _postProcessTarget?.Dispose();
        _textures.Dispose();
        _sliceGeometry.Clear();
        ClearGeometryCache();
        ClearCollectionCache(); ClearSortCache();
        _utilityUploads.Dispose(); _utilityMesh.Dispose();
        _triangleUploads.Dispose(); _lineUploads.Dispose(); _texturedUploads?.Dispose();
        _texturedMesh?.Dispose();
        _texturedProgram?.Dispose();
        _lineMesh.Dispose();
        _triangleMesh.Dispose();
        _program.Dispose();
        _submissions.Clear();
        _quadData.Clear(); _quadData.TrimExcess();
        foreach (var payload in _quadPayloads) payload.Unbind();
        _quadPayloads.Clear();
        _hierarchyOrder = EmptyHierarchy;
        _orderedCameras.Clear();
        foreach (var batch in _batches) batch.Clear();
        DisposeMasks();
        if (_ownsDevice) _device.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static Quad CreateQuad(
        Vector2 center,
        Fix64 rotation,
        Vector2 size,
        Color color,
        string texture,
        Rect uv,
        bool flipX,
        bool flipY,
        bool isTextured) => new(center, rotation, size, color, color, color, color,
            texture, uv, flipX, flipY, isTextured);

    private readonly struct QuadProcessor
    {
        private readonly ISceneRenderContributor2D _legacy;
        private readonly ISceneQuadReferenceProcessor2D? _referenced;
        public QuadProcessor(ISceneRenderContributor2D contributor)
        {
            _legacy = contributor;
            _referenced = contributor as ISceneQuadReferenceProcessor2D;
        }
        public void Process(scoped ref Quad quad, GameObject target)
        {
            if (_referenced is { } referenced) referenced.ProcessQuadReferenced(ref quad, target);
            else quad = _legacy.ProcessQuad(quad, target);
        }
    }

    private void ApplySurfaceEffectsReferenced(scoped ref Quad quad, GameObject target)
    {
        foreach (var contributor in _processQuadContributors) contributor.Process(ref quad, target);
    }
    private static Quad WithSpriteAxes(Quad quad, Transform transform, Vector2 localCenter, Vector2 localSize, RenderCamera camera)
    {
        var matrix = transform.localToWorldMatrix;
        var x = matrix.TransformVector(new Vector2(localSize.x * Fix64.Half, 0));
        var y = matrix.TransformVector(new Vector2(0, localSize.y * Fix64.Half));
        var signs = transform.lossyScale;
        if (signs.x < 0) x = -x;
        if (signs.y < 0) y = -y;
        return quad with { Center = camera.SnapWorldPoint(matrix.TransformPoint(localCenter)), WorldAxisX = x, WorldAxisY = y };
    }

    private object ReusePayload(in Quad quad)
    {
        var dataIndex = _quadData.Add(quad);
        // Core-only frames consume numeric indices directly. Material/effect extensions
        // receive the existing class facade when contributors participate in the frame.
        if (_frameContributors.Count == 0) { _quadPayloadCount++; return CoreQuadPayload; }
        if (_quadPayloadCount == _quadPayloads.Count) _quadPayloads.Add(new QuadPayload());
        var payload = _quadPayloads[_quadPayloadCount++];
        _quadPayloadUsedCount = Math.Max(_quadPayloadUsedCount, _quadPayloadCount);
        payload.Bind(_quadData, dataIndex);
        return payload;
    }

    private bool TryGetQuad(in RenderSubmission2D submission, out Quad quad)
    {
        var index = submission.SceneQuadIndex - 1;
        if ((uint)index < (uint)_quadData.Count && (ReferenceEquals(submission.Payload, CoreQuadPayload) ||
            (uint)index < (uint)_quadPayloads.Count && ReferenceEquals(submission.Payload, _quadPayloads[index])))
        { quad = _quadData[index]; return true; }
        if (submission.Payload is QuadPayload payload) { quad = payload.Value; return true; }
        quad = default; return false;
    }


    private readonly record struct RenderWorkStatistics(
        int VisibleSubmissionCount,
        int BatchCount);

    private readonly record struct CoreBatchRange(int FirstVertex, int VertexCount, bool Textured, string? Texture, bool Opaque);

    private readonly record struct CameraRenderItem(Camera2D Camera, int Sequence);

    private readonly struct RenderScope(PortableSceneRenderer owner, SceneRenderDiagnostics.CaptureScope diagnostic) : IDisposable
    {
        public void Dispose()
        {
            try { owner.ExitRender(); diagnostic.CleanupCompleted(); }
            finally { diagnostic.Dispose(); }
        }
    }


    private readonly Scene[] _singleScene = new Scene[1];

    private sealed class GeometrySink(PortableSceneRenderer owner) : ISceneGeometrySink2D, ISceneFillBarSink2D, ISceneQuadBatchSink2D, ISceneOwnedQuadBatchSink2D
    {
        public RenderCamera Camera { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public long Sequence { get; private set; }
        private bool _fillBarCullMatches;
        internal void Reset(RenderCamera camera, int width, int height, long sequence)
        {
            Camera = camera; Width = width; Height = height; Sequence = sequence;
            _fillBarCullMatches = camera.Rotation % 360 == 0;
        }
        public void SubmitFillBar(Renderer2D renderer, Vector2 center, Vector2 size,
            Color color, RenderBatchKey2D batch)
        {
            // At full turns both visibility queries use identical fixed-point
            // arithmetic. Other rotations retain the original first query:
            // Transform.RotateVector has exact cardinal-angle branches.
            if (!_fillBarCullMatches && !Camera.IsVisible(center, 0, size, Width, Height)) return;
            Submit(renderer, SceneQuad2D.Create(center, 0, size, color, "",
                new Rect(0, 0, 1, 1), false, false, false), batch);
        }
        public void SubmitQuads(Renderer2D renderer, List<SceneQuadSubmission2D> quads, bool snapCenters)
        {
            // Keep the original enumerator, including mutation checks after a
            // callback, even if that callback changes the final list element.
            foreach (var item in quads)
            {
                var quad = item.Quad;
                if (snapCenters)
                {
                    var camera = Camera;
                    if (camera.Policy is not null) quad = quad with { Center = camera.SnapWorldPoint(quad.Center) };
                }
                // The local copy is intentional: policy/contributor callbacks
                // may mutate or grow the source List while this item is active.
                SubmitCore(renderer, in quad, item.Key);
            }
        }

        public void SubmitOwnedQuads(Renderer2D renderer, SpriteQuadBuffer2D quads, bool snapCenters)
        {
            var cursor = quads.CreateCursor();
            while (cursor.MoveNext(out var quad, out var key))
            {
                if (snapCenters)
                {
                    var camera = Camera;
                    if (camera.Policy is not null) quad = quad with { Center = camera.SnapWorldPoint(quad.Center) };
                }
                SubmitCore(renderer, in quad, key);
            }
        }

        public void Submit(Renderer2D renderer, SceneQuad2D quad, RenderBatchKey2D batchKey) =>
            SubmitCore(renderer, in quad, batchKey);

        private void SubmitCore(Renderer2D renderer, in SceneQuad2D quad, RenderBatchKey2D batchKey)
        {
            if (!owner._culling.IsVisibleCore(in quad)) return;
            // Preserve the stable input and the live renderer reads after all
            // contributor callbacks without copying the unmodified value again.
            SceneQuad2D processed;
            scoped ref readonly SceneQuad2D submitted = ref quad;
            if (renderer.receiveLighting && quad.ApplySurfaceEffects)
            {
                processed = quad;
                owner.ApplySurfaceEffectsReferenced(ref processed, renderer.gameObject);
                submitted = ref processed;
            }
            owner._submissions.Add(new RenderSubmission2D(new RenderSortKey2D(renderer.sortingLayer,
                renderer.orderInLayer, owner._hierarchyOrder.GetValueOrDefault(renderer.gameObject),
                renderer.TransparencyUnchecked(submitted.ColorA), Sequence++), batchKey, owner.ReusePayload(in submitted))
                { SceneQuadIndex = owner._quadPayloadCount });
        }
    }
}
