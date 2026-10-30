using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer : IDisposable
{

    private static readonly GraphicsVertexLayout VertexLayout = new(
        6 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float))
        ]);

    private static readonly GraphicsVertexLayout TextureVertexLayout = new(
        8 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
        ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _shader;
    private readonly IGraphicsMesh _mesh;
    private readonly GraphicsMeshUploadCache _meshUploads;
    private GraphicsMeshUploadCache? _textureUploads;
    private readonly List<RenderBatch> _batches = [];
    private readonly ScreenSpaceBatchPlanner _batchPlanner = new();
    private readonly Stack<RenderBatch> _batchPool = [];
    // Keep the old worst-case 16 MiB total vertex retention, but allow one
    // dense glyph batch to use more of it instead of rebuilding it every frame.
    private const int MaximumPooledVertexFloats = 4 * 1024 * 1024;
    private const int MaximumPooledBatchVertexFloats = 256 * 1024;
    private const int MaximumPooledBatches = 256;
    private int _pooledVertexCapacity;
    private readonly List<float> _solidVertices = [], _texturedVertices = [];
    private readonly List<UIRenderCommandList> _renderLists = [];
    private readonly IUIRenderResourceResolver _resourceResolver;
    private readonly ITextRasterizer? _textResourceResolver;
    private readonly BoundedLruCache<string, IGraphicsTexture2D> _textures = new(StringComparer.Ordinal, texture => texture.Dispose());
    private readonly BoundedLruCache<BasicTextKey, IGraphicsTexture2D> _textTextures =
        new(release: texture => texture.Dispose());
    // Advanced text is keyed by its value type instead of a formatted string.
    // HUD text changes frequently; building a long cache key per label/frame
    // otherwise becomes a surprisingly large source of managed garbage.
    private readonly BoundedLruCache<AdvancedTextKey, IGraphicsTexture2D> _advancedTextures =
        new(release: texture => texture.Dispose());
    private long _textureCacheBudget = 32 * 1024 * 1024;
    public long textureCacheBudgetBytes { get => _textureCacheBudget; set { ArgumentOutOfRangeException.ThrowIfNegative(value); _textureCacheBudget = value; } }
    public long cachedTextureBytes => _textures.RetainedBytes + _textTextures.RetainedBytes + _advancedTextures.RetainedBytes;
    public int cachedTextureCount => _textures.Count + _textTextures.Count + _advancedTextures.Count;
    private readonly List<UIDocument> _documents = [];
    private readonly bool _ownsDevice;
    private IGraphicsProgram? _textureShader;
    private IGraphicsProgram? _sdfShader;
    private IGraphicsMesh? _textureMesh;
    private UIRenderStatistics _lastStatistics;
    private bool _disposed;
    private readonly TextRasterProbe? _textRasterProbe = TextRasterProbe.Create();

    public GraphicsBackend Backend
    {
        get { return _device.Backend; }
    }
    public GraphicsDeviceCapabilities Capabilities
    {
        get { return _device.Capabilities; }
    }
    public UIRenderStatistics LastStatistics
    {
        get { return _lastStatistics; }
        private set => _lastStatistics = value;
    }

    public UIElementsRenderer(
        IGraphicsDevice device,
        IUIRenderResourceResolver? resourceResolver = null,
        bool ownsDevice = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Capabilities.Require(
            GraphicsDeviceFeatures.Rasterization |
            GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers |
            GraphicsDeviceFeatures.AlphaBlending |
            GraphicsDeviceFeatures.ScissorRectangles);
        _device = device;
        _resourceResolver = resourceResolver ?? UIElementsRuntimeServices.RenderResourceResolver;
        _textResourceResolver = _resourceResolver as ITextRasterizer ??
                                UIElementsRuntimeServices.TextResourceResolver;
        _ownsDevice = ownsDevice;
        _sdfCommandGeometry = new(ReferenceEqualityComparer.Instance, ReleaseSdfGeometry);
        var (vertexShader, fragmentShader) = ResolveShaderSources(device, textured: false);
        _shader = device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.UIElements",
            vertexShader,
            fragmentShader));
        _mesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.UIElements.DynamicMesh",
            ReadOnlyMemory<float>.Empty,
            VertexLayout,
            usage: GraphicsBufferUsage.Dynamic));
        _meshUploads = new GraphicsMeshUploadCache(_mesh);
        CaptureWebSolidSources(new(vertexShader, fragmentShader));
    }

    public void Render(Scene scene, int width, int height)
        => Render(scene, width, height, new GraphicsRect(0, 0, width, height));

    public void Render(Scene scene, int width, int height, GraphicsRect viewport)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _documents.Clear();
        _documents.AddRange(UIDocumentInputRouter.Collect(scene));
        _documents.RemoveAll(static item => !item.isVisible);
        var renderLists = _renderLists; renderLists.Clear();
        foreach (var document in _documents)
            renderLists.Add(UIRenderListBuilder.Build(
                document.rootVisualElement, width, height, document.ResolveScale(width, height)));
        RenderCore(renderLists, width, height, viewport);
    }

    public void Render(VisualElement root, int width, int height, Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        RenderCore([UIRenderListBuilder.Build(root, width, height, scale)], width, height);
    }

    public void Render(IEnumerable<UIRenderCommandList> renderLists, int width, int height)
    {
        RenderCore(renderLists, width, height);
    }

    public void RenderCommands(IEnumerable<UIRenderCommand> commands, int width, int height,
        GraphicsRect viewport)
    {
        RenderCore(commands, width, height, viewport);
    }

    private void RenderCore(IEnumerable<UIRenderCommandList> renderLists, int width, int height,
        GraphicsRect? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(renderLists);
        ObjectDisposedException.ThrowIf(_disposed, this);
        RenderCore(new CommandCursor(renderLists), width, height, viewport);
    }

    private void RenderCore(IEnumerable<UIRenderCommand> commands, int width, int height,
        GraphicsRect? viewport = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        RenderCore(new CommandCursor(commands), width, height, viewport);
    }

    private void RenderCore(CommandCursor commands, int width, int height, GraphicsRect? viewport)
    {
        using var diagnosticRender = SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiRender);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        TrimTextureCache();
        BatchBuildResult build;
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiBuildBatches))
        { build = BuildBatches(commands, width, height); }
        TrimSdfGeometry();
        if (build.Batches.Count == 0)
        {
            LastStatistics = new UIRenderStatistics(
                build.CommandCount, 0, 0, 0, build.UnresolvedImages)
                { TextGeometryCacheHits = _textGeometryCacheHits, TextGeometryRebuiltGlyphs = _textGeometryRebuiltGlyphs };
            return;
        }

        var targetViewport = viewport ?? new GraphicsRect(0, 0, width, height);
        _device.SetViewport(targetViewport);
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiWebSdfClip))
        { PrepareWebClipGeometry(build.Batches, width, height, targetViewport); }
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiWebSolidClip))
        { PrepareWebSolidClipGeometry(build.Batches, width, height, targetViewport); }
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiUpload))
        {
            UploadFrameGeometry(build.Batches);
            UploadWebClipGeometry();
            UploadWebSolidClipGeometry();
        }
        IGraphicsProgram? activeProgram = null;
        var drawCalls = 0;
        var opaqueSolidDraws = 0;
        var vertexCount = 0;
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiDraw))
        {
        try
        {
            for (var batchIndex = 0; batchIndex < build.Batches.Count; batchIndex++)
            {
                var batch = build.Batches[batchIndex];
                if (TryDrawWebSolidClipRun(build.Batches, ref batchIndex, width, height, ref activeProgram, out var solidClippedVertices))
                {
                    vertexCount += solidClippedVertices; drawCalls++; opaqueSolidDraws++;
                    continue;
                }
                if (TryDrawWebClipRun(build.Batches, ref batchIndex, width, height, ref activeProgram, out var clippedVertices))
                {
                    vertexCount += clippedVertices; drawCalls++;
                    if (batch.Opaque) opaqueSolidDraws++;
                    continue;
                }
                _device.SetBlendMode(batch.Opaque ? GraphicsBlendMode.Disabled : GraphicsBlendMode.AlphaBlend);
                _device.SetScissor(ToGraphicsRect(batch.ClipRect,
                    width, height, targetViewport.Width, targetViewport.Height));
                if (batch.Texture is null)
                {
                    if (!ReferenceEquals(activeProgram, _shader))
                    {
                        activeProgram = _shader;
                        BindProgram(activeProgram, width, height);
                    }
                    _device.Draw(_mesh, batch.VertexCount, batch.FirstVertex);
                    vertexCount += batch.VertexCount;
                }
                else
                {
                    EnsureTexturePipeline();
                    var program = batch.SdfThreshold is not null ? EnsureSdfPipeline() : _textureShader!;
                    if (!ReferenceEquals(activeProgram, program))
                    {
                        activeProgram = program;
                        BindProgram(activeProgram, width, height);
                        activeProgram.SetInt("uTexture", 0);
                    }
                    if (batch.SdfThreshold is { } threshold) activeProgram.SetFloat("uSdfThreshold", threshold);
                    _device.BindTexture(0, batch.Texture);
                    _device.Draw(_textureMesh!, batch.VertexCount, batch.FirstVertex);
                    vertexCount += batch.VertexCount;
                }
                drawCalls++;
                if (batch.Opaque) opaqueSolidDraws++;
            }
        }
        finally
        {
            _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
            _device.SetScissor(null);
            _device.SetDepthState(GraphicsDepthState.Default);
        }
        }
        LastStatistics = new UIRenderStatistics(
            build.CommandCount,
            build.Batches.Count,
            drawCalls,
            vertexCount,
            build.UnresolvedImages) { OpaqueSolidDrawCalls = opaqueSolidDraws,
                TextGeometryCacheHits = _textGeometryCacheHits, TextGeometryRebuiltGlyphs = _textGeometryRebuiltGlyphs };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _textRasterProbe?.Report();
        _textures.Clear(); _textTextures.Clear(); _advancedTextures.Clear(); _sdfLayouts.Clear();
        ClearSdfGeometry();
        _textureMesh?.Dispose();
        _textureShader?.Dispose();
        _sdfShader?.Dispose();
        _meshUploads.Dispose(); _textureUploads?.Dispose();
        DisposeWebClip();
        DisposeWebSolidClip();
        _batches.Clear(); _batchPlanner.Reset(); _batchPool.Clear(); _pooledVertexCapacity = 0; _renderLists.Clear(); _documents.Clear();
        _solidVertices.Clear(); _texturedVertices.Clear();
        _mesh.Dispose();
        _shader.Dispose();
        if (_ownsDevice) _device.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public bool InvalidateTexture(string source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!_textures.Remove(source, out var texture)) return false;
        texture.Dispose();
        return true;
    }

    public void ClearTextureCache()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _textures.Clear(); _textTextures.Clear(); _sdfLayouts.Clear();
        ClearSdfGeometry();
    }

    private void TrimTextureCache()
    {
        _textures.Trim(512, _textureCacheBudget);
        _textTextures.Trim(256, Math.Min(_textureCacheBudget / 2, 16 * 1024 * 1024));
        _advancedTextures.Trim(256, Math.Min(_textureCacheBudget / 2, 16 * 1024 * 1024));
    }

    private BatchBuildResult BuildBatches(CommandCursor commands, int width, int height)
    {
        _batchPlanner.Reset();
        foreach (var previous in _batches)
        {
            previous.ClearReferences();
            var capacity = previous.Vertices.Capacity;
            if (_batchPool.Count < MaximumPooledBatches && capacity <= MaximumPooledBatchVertexFloats &&
                capacity <= MaximumPooledVertexFloats - _pooledVertexCapacity)
            {
                _batchPool.Push(previous);
                _pooledVertexCapacity += capacity;
            }
        }
        var batches = _batches; batches.Clear();
        var commandCount = 0;
        var unresolvedImages = 0;
        _textGeometryCacheHits = _textGeometryRebuiltGlyphs = 0;
        UIRenderBudgetDiagnostics.culledCommands = 0;
        UIRenderBudgetDiagnostics.translucentRectangleCoverage = 0;
        try
        {
        while (commands.MoveNext())
        {
            var command = commands.Current;
            commandCount++;
            if (command.ClipRect.Width <= 0 || command.ClipRect.Height <= 0) { UIRenderBudgetDiagnostics.culledCommands++; continue; }
            if (command.Type != UIRenderCommandType.Text && (command.Rect.Width <= 0 || command.Rect.Height <= 0 ||
                command.Rect.X + command.Rect.Width <= command.ClipRect.X ||
                command.Rect.Y + command.Rect.Height <= command.ClipRect.Y ||
                command.Rect.X >= command.ClipRect.X + command.ClipRect.Width ||
                command.Rect.Y >= command.ClipRect.Y + command.ClipRect.Height)) { UIRenderBudgetDiagnostics.culledCommands++; continue; }
            if (command.Type == UIRenderCommandType.SolidRect && command.Color.A is > 0 and < 255)
            {
                var x0 = Fix64.Max(0, Fix64.Max(command.Rect.X, command.ClipRect.X));
                var y0 = Fix64.Max(0, Fix64.Max(command.Rect.Y, command.ClipRect.Y));
                var x1 = Fix64.Min(width, Fix64.Min(command.Rect.X + command.Rect.Width, command.ClipRect.X + command.ClipRect.Width));
                var y1 = Fix64.Min(height, Fix64.Min(command.Rect.Y + command.Rect.Height, command.ClipRect.Y + command.ClipRect.Height));
                UIRenderBudgetDiagnostics.translucentRectangleCoverage += (double)Fix64.Max(0, x1 - x0) * (double)Fix64.Max(0, y1 - y0) / ((double)width * height);
            }
            if (command.Type == UIRenderCommandType.Image)
            {
                if (TryResolveTexture(command.Content, out var texture))
                {
                    var batch = GetBatch(batches, command.ClipRect, texture, command.Rect);
                    AddTexturedQuad(batch.Vertices, command.Rect, command.Color);
                }
                else
                {
                    unresolvedImages++;
                    AddMissingImage(GetBatch(batches, command.ClipRect, null, command.Rect).Vertices, command.Rect);
                }
                continue;
            }

            if (command.Type == UIRenderCommandType.Text && TryAddAdvancedText(batches, command)) continue;
            if (command.Type == UIRenderCommandType.Text && TryAddSdfText(batches, command)) continue;
            if (command.Type == UIRenderCommandType.Text && TryResolveTextTexture(command, out var textTexture))
            {
                var textBatch = GetBatch(batches, command.ClipRect, textTexture, command.Rect);
                AddTexturedQuad(textBatch.Vertices, command.Rect, command.Color);
                continue;
            }

            var output = GetBatch(batches, command.ClipRect, null,
                command.Type == UIRenderCommandType.Text ? command.ClipRect : command.Rect,
                opaque: CanReplaceSolid(command)).Vertices;
            if (command.Type == UIRenderCommandType.SolidRect)
                AddQuad(output, command.Rect, command.Color);
            else if (command.Type == UIRenderCommandType.Text)
                AddText(output, command.Content, command.Rect, command.Color, command.FontSize, command.Element is Label);
        }
        }
        finally { commands.Dispose(); }
        batches.RemoveAll(batch => batch.Vertices.Count == 0);
        return new BatchBuildResult(batches, commandCount, unresolvedImages);
    }

    // Commands emitted by the layout engine are arrays. Keep their traversal,
    // and ordinary List<T> traversal, on the stack instead of boxing one array
    // enumerator per render batch. Unknown/derived collections retain their
    // actual IEnumerable implementation, including callbacks and exceptions.
    private struct ValueEnumerator<T>
    {
        private IEnumerable<T>? _source;
        private readonly T[]? _array;
        private readonly List<T>? _list;
        private List<T>.Enumerator _listEnumerator;
        private IEnumerator<T>? _enumerator;
        private int _index;
        private bool _started;
        internal T Current { get; private set; } = default!;

        internal ValueEnumerator(IEnumerable<T> source)
        {
            _source = source;
            _array = source as T[];
            _list = source?.GetType() == typeof(List<T>) ? (List<T>)source : null;
        }

        internal bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                if (_list is not null) _listEnumerator = _list.GetEnumerator();
                else if (_array is null) _enumerator = _source!.GetEnumerator();
            }
            if (_array is not null)
            {
                if (_index == _array.Length) return false;
                Current = _array[_index++];
                return true;
            }
            if (_list is not null)
            {
                if (!_listEnumerator.MoveNext()) return false;
                Current = _listEnumerator.Current;
                return true;
            }
            if (!_enumerator!.MoveNext()) return false;
            Current = _enumerator.Current;
            return true;
        }

        internal void Dispose()
        {
            if (!_started) return;
            if (_list is not null) _listEnumerator.Dispose();
            else _enumerator?.Dispose();
            _enumerator = null;
            _source = null;
            _started = false;
        }
    }

    private struct CommandCursor
    {
        private ValueEnumerator<UIRenderCommandList> _lists;
        private ValueEnumerator<UIRenderCommand> _commands;
        private readonly bool _nested;
        private bool _hasCommands;
        internal UIRenderCommand Current => _commands.Current;

        internal CommandCursor(IEnumerable<UIRenderCommand> commands) => _commands = new(commands);
        internal CommandCursor(IEnumerable<UIRenderCommandList> lists)
        {
            _lists = new(lists);
            _nested = true;
        }

        internal bool MoveNext()
        {
            if (!_nested) return _commands.MoveNext();
            while (true)
            {
                if (_hasCommands)
                {
                    if (_commands.MoveNext()) return true;
                    _commands.Dispose();
                    _hasCommands = false;
                }
                if (!_lists.MoveNext()) { Dispose(); return false; }
                _commands = new(_lists.Current.Commands);
                _hasCommands = true;
            }
        }

        internal void Dispose()
        {
            if (!_nested) { _commands.Dispose(); return; }
            if (_hasCommands) { _commands.Dispose(); _hasCommands = false; }
            _lists.Dispose();
        }
    }

    private void UploadFrameGeometry(List<RenderBatch> batches)
    {
        var solidVertices = _solidVertices; solidVertices.Clear();
        var texturedVertices = _texturedVertices; texturedVertices.Clear();
        var batchIndex = 0;
        var hasTexturedBatches = false;
        foreach (var batch in batches)
        {
            var clipIndex = batchIndex++;
            if (batch.Texture is not null)
            {
                hasTexturedBatches = true;
                // This batch is drawn exclusively from the clip mesh below.
                // Preserve the old stream only for batches using its fallback.
                if (clipIndex < _webClipRanges.Count && _webClipRanges[clipIndex].Valid) continue;
            }
            if (clipIndex < _webSolidClipRanges.Count && _webSolidClipRanges[clipIndex].Valid) continue;
            var destination = batch.Texture is null ? solidVertices : texturedVertices;
            var stride = batch.Texture is null ? VertexLayout.StrideBytes : TextureVertexLayout.StrideBytes;
            batch.FirstVertex = destination.Count * sizeof(float) / stride;
            destination.AddRange(batch.Vertices);
        }
        if (solidVertices.Count > 0) _meshUploads.Update(CollectionsMarshal.AsSpan(solidVertices));
        if (!hasTexturedBatches) return;
        // Keep texture-pipeline capability/shader validation and ownership even
        // when every textured batch uses the clip mesh for this frame.
        EnsureTexturePipeline();
        if (texturedVertices.Count <= 0) return;
        _textureUploads ??= new GraphicsMeshUploadCache(_textureMesh!);
        _textureUploads.Update(CollectionsMarshal.AsSpan(texturedVertices));
    }

    // Kept internal for pixel-equivalence regression against sequential painting.
    internal bool EnableDisjointBatching { get; set; } = true;

    private RenderBatch GetBatch(
        List<RenderBatch> batches,
        UIElementRect clipRect,
        IGraphicsTexture2D? texture, UIElementRect bounds, float? sdfThreshold = null, bool opaque = false)
    {
        // Keep the whole numeric search in stable Core, including cache hits.
        // The planner retains only the twelve batches this search may revisit.
        var index = _batchPlanner.FindOrAdd(
            new ScreenSpaceBatchRect(clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height), texture,
            new ScreenSpaceBatchRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            sdfThreshold, EnableDisjointBatching);
        if (index < batches.Count)
        {
            var candidate = batches[index];
            candidate.IncludeOpacity(opaque);
            return candidate;
        }
        var batch = _batchPool.TryPop(out var reused) ? reused : new RenderBatch();
        if (reused is not null) _pooledVertexCapacity -= reused.Vertices.Capacity;
        batch.Reset(clipRect, texture, sdfThreshold, opaque); batches.Add(batch);
        return batch;
    }

    private static void AddText(
        List<float> output,
        string text,
        UIElementRect rect,
        UIColor color,
        float requestedSize,
        bool wrap)
    {
        var fontSize = requestedSize > 0 ? requestedSize : 14;
        if (wrap)
        {
            var columns = Math.Max(1, (int)(((float)rect.Width - Math.Min(12, fontSize * 2 / 3)) / (fontSize * 6 / 7)));
            var lines = new List<string>();
            foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
            {
                var remaining = paragraph;
                while (remaining.Length > columns)
                {
                    var split = remaining.LastIndexOf(' ', columns, columns);
                    if (split <= 0) split = columns;
                    lines.Add(remaining[..split]);
                    remaining = remaining[split..].TrimStart();
                }
                lines.Add(remaining);
            }
            var fittedSize = Math.Min(fontSize, (float)rect.Height * 7 / Math.Max(7, lines.Count * 8 - 1));
            AddText(output, string.Join('\n', lines), rect, color, fittedSize, wrap: false);
            return;
        }
        var cell = Math.Max(0.125f, fontSize / 7f);
        var glyphWidth = cell * 5;
        var advance = cell * 6;
        var glyphHeight = cell * 7;
        var inset = Math.Min(6, fontSize / 3);
        var x = (float)rect.X + inset;
        var lineCount = text.Count(character => character == '\n') + 1;
        var textHeight = lineCount * glyphHeight + (lineCount - 1) * cell;
        var y = (float)rect.Y + Math.Max(0, ((float)rect.Height - textHeight) * 0.5f);
        foreach (var character in text.Replace("\r", string.Empty, StringComparison.Ordinal))
        {
            if (character == '\n')
            {
                x = (float)rect.X + inset;
                y += glyphHeight + cell;
                continue;
            }
            if (x + glyphWidth > (float)(rect.X + rect.Width)) break;
            if (character != ' ')
            {
                var pattern = BuiltinFont.Pattern(character);
                for (var row = 0; row < 7; row++)
                for (var column = 0; column < 5; column++)
                {
                    if (pattern[row * 5 + column] != '1') continue;
                    AddQuad(output, new UIElementRect(
                        (Fix64)(x + column * cell),
                        (Fix64)(y + row * cell),
                        (Fix64)(cell * 0.82f),
                        (Fix64)(cell * 0.82f)), color);
                }
            }
            x += advance;
        }
    }

    private static void AddQuad(List<float> output, UIElementRect rect, UIColor color)
    {
        var x0 = (float)rect.X;
        var x1 = (float)(rect.X + rect.Width);
        var y0 = (float)rect.Y;
        var y1 = (float)(rect.Y + rect.Height);
        var rgba = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        ScreenSpaceGeometryWriter.AppendSolidQuad(output, x0, y0, x1, y1, rgba);
    }

    private static void AddTexturedQuad(List<float> output, UIElementRect rect, UIColor color)
    {
        var x0 = (float)rect.X;
        var x1 = (float)(rect.X + rect.Width);
        var y0 = (float)rect.Y;
        var y1 = (float)(rect.Y + rect.Height);
        var rgba = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        ScreenSpaceGeometryWriter.AppendTexturedQuad(output, x0, y0, x1, y1, rgba, 0, 0, 1, 1);
    }

    private static void AddMissingImage(List<float> output, UIElementRect rect)
    {
        var halfWidth = rect.Width / 2;
        var halfHeight = rect.Height / 2;
        var dark = new UIColor(48, 50, 54);
        var accent = new UIColor(194, 63, 86);
        AddQuad(output, new UIElementRect(rect.X, rect.Y, halfWidth, halfHeight), dark);
        AddQuad(output, new UIElementRect(rect.X + halfWidth, rect.Y, rect.Width - halfWidth, halfHeight), accent);
        AddQuad(output, new UIElementRect(rect.X, rect.Y + halfHeight, halfWidth, rect.Height - halfHeight), accent);
        AddQuad(output, new UIElementRect(
            rect.X + halfWidth,
            rect.Y + halfHeight,
            rect.Width - halfWidth,
            rect.Height - halfHeight), dark);
    }

    private bool TryResolveTexture(string source, out IGraphicsTexture2D? texture)
    {
        texture = null;
        if (!_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        if (_textures.TryGetValue(source, out texture)) return true;
        TextureImageData? imported = null;
        var importedResolved = false;
        RasterizedImage data = default;
        if (_resourceResolver is FileUIRenderResourceResolver fileResolver)
        {
            bool resolved;
            using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiImageResolve))
            { resolved = fileResolver.TryResolveTextureData(source, out imported, out data); }
            if (!resolved) return false;
            importedResolved = imported is not null;
        }
        else
        {
            if (_resourceResolver is IUIImportedTextureResolver importedResolver)
            {
                using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiImageResolve))
                { importedResolved = importedResolver.TryResolveImportedTexture(source, out imported); }
            }
            bool rasterized;
            if (!importedResolved)
            {
                using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiImageResolve))
                { rasterized = _resourceResolver.TryResolveTexture(source, out data); }
                if (!rasterized) return false;
            }
        }
        if (importedResolved)
        {
            using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiImageUpload))
            {
                texture = TextureUpload.Create(_device, $"BEngine.UIElements.{source}", imported!);
                _textures.Add(source, texture, texture.Description.DataByteSize);
            }
            return true;
        }
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiImageUpload))
        {
            data.Validate();
            texture = _device.CreateTexture2D(
                $"BEngine.UIElements.{source}",
                new GraphicsTextureDescription(
                    data.Width,
                    data.Height,
                    data.Format,
                    GraphicsTextureUsage.Sampled,
                    GraphicsTextureFilter.Linear,
                    GraphicsTextureFilter.Linear,
                    GraphicsTextureAddressMode.ClampToEdge),
                data.Pixels.Span);
            _textures.Add(source, texture, (long)texture.Description.Width * texture.Description.Height * 4);
        }
        return true;
    }

    private bool TryResolveTextTexture(UIRenderCommand command, out IGraphicsTexture2D? texture)
    {
        texture = null;
        if (_textResourceResolver is not { } textResolver ||
            !_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        var width = Math.Max(1, (int)Math.Ceiling((double)command.Rect.Width));
        var height = Math.Max(1, (int)Math.Ceiling((double)command.Rect.Height));
        var wordWrap = command.Element is Label;
        var key = new BasicTextKey(width, height, command.FontSize, wordWrap, command.Content);
        if (_textTextures.TryGetValue(key, out texture)) return true;
        var started = _textRasterProbe is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        RasterizedImage data;
        bool resolved;
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiTextRaster))
        { resolved = textResolver.TryResolveText(command.Content, width, height, command.FontSize, wordWrap, out data); }
        if (!resolved) return false;
        var rasterized = _textRasterProbe is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        using (SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiTextUpload))
        {
            data.Validate();
            texture = _device.CreateTexture2D(
                "BEngine.UIElements.Text",
                new GraphicsTextureDescription(data.Width, data.Height, data.Format,
                    GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear),
                data.Pixels.Span);
            _textTextures.Add(key, texture, (long)texture.Description.Width * texture.Description.Height * 4);
        }
        _textRasterProbe?.Record(started, rasterized, System.Diagnostics.Stopwatch.GetTimestamp(),
            command.Element.name, key);
        return true;
    }

    private void EnsureTexturePipeline()
    {
        if (_textureShader is not null && _textureMesh is not null) return;
        _device.Capabilities.Require(GraphicsDeviceFeatures.SampledTextures);
        var (vertexShader, fragmentShader) = ResolveShaderSources(_device, textured: true);
        _textureShader = _device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.UIElements.Texture",
            vertexShader,
            fragmentShader));
        try
        {
            _textureMesh = _device.CreateMesh(new GraphicsMeshDescription(
                "BEngine.UIElements.TextureDynamicMesh",
                ReadOnlyMemory<float>.Empty,
                TextureVertexLayout,
                usage: GraphicsBufferUsage.Dynamic));
        }
        catch
        {
            _textureShader.Dispose();
            _textureShader = null;
            throw;
        }
    }

    private static void BindProgram(IGraphicsProgram program, int width, int height)
    {
        program.Bind();
        program.SetFloat("uViewportWidth", width);
        program.SetFloat("uViewportHeight", height);
    }

    private static GraphicsRect ToGraphicsRect(UIElementRect rect,
        int logicalWidth, int logicalHeight, int viewportWidth, int viewportHeight)
    {
        var scaleX = Math.Max(1, viewportWidth) / (double)Math.Max(1, logicalWidth);
        var scaleY = Math.Max(1, viewportHeight) / (double)Math.Max(1, logicalHeight);
        var left = Math.Clamp((int)Math.Floor((double)rect.X * scaleX), 0, Math.Max(0, viewportWidth));
        var top = Math.Clamp((int)Math.Floor((double)rect.Y * scaleY), 0, Math.Max(0, viewportHeight));
        var right = Math.Clamp((int)Math.Ceiling((double)(rect.X + rect.Width) * scaleX),
            left, Math.Max(left, viewportWidth));
        var bottom = Math.Clamp((int)Math.Ceiling((double)(rect.Y + rect.Height) * scaleY),
            top, Math.Max(top, viewportHeight));
        return new GraphicsRect(left, top, right - left, bottom - top);
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaderSources(
        IGraphicsDevice device,
        bool textured)
    {
        var pair = GraphicsUtility.Current.ResolveShaderVariant(device.Capabilities, textured ? "Shaders/UIElements/Textured" : "Shaders/UIElements/Solid");
        return (pair.Vertex, pair.Fragment);
    }

    private sealed class RenderBatch
    {
        public UIElementRect ClipRect { get; private set; }
        public IGraphicsTexture2D? Texture { get; private set; }
        public float? SdfThreshold { get; private set; }
        public bool Opaque { get; private set; }
        public List<float> Vertices { get; } = [];
        public int FirstVertex { get; set; }
        public int VertexCount => Vertices.Count * sizeof(float) /
                                  (Texture is null ? VertexLayout.StrideBytes : TextureVertexLayout.StrideBytes);
        public void Reset(UIElementRect clip, IGraphicsTexture2D? texture, float? threshold, bool opaque)
        { ClipRect = clip; Texture = texture; SdfThreshold = threshold; Opaque = opaque; FirstVertex = 0; Vertices.Clear(); }
        public void IncludeOpacity(bool opaque) => Opaque &= opaque;
        public void ClearReferences() { Texture = null; Vertices.Clear(); }
    }

    private readonly record struct BasicTextKey(
        int Width, int Height, float Size, bool Wrap, string Text);

    internal readonly record struct AdvancedTextKey(
        SdfFontAsset Font, int Width, int Height, float Size, bool Wrap,
        TextDirectionMode Direction, UIColor Color, string Text);

    private readonly record struct BatchBuildResult(
        List<RenderBatch> Batches,
        int CommandCount,
        int UnresolvedImages);

    private static class BuiltinFont
    {
        private const string Missing = "11111100010001000100001000000000100";
        private static readonly Dictionary<char, string> Glyphs = new()
        {
            ['A']="01110100011000111111100011000110001", ['B']="11110100011000111110100011000111110",
            ['C']="01111100001000010000100001000001111", ['D']="11110100011000110001100011000111110",
            ['E']="11111100001000011110100001000011111", ['F']="11111100001000011110100001000010000",
            ['G']="01111100001000010111100011000101111", ['H']="10001100011000111111100011000110001",
            ['I']="11111001000010000100001000010011111", ['J']="00111000100001000010100101001001100",
            ['K']="10001100101010011000101001001010001", ['L']="10000100001000010000100001000011111",
            ['M']="10001110111010110101100011000110001", ['N']="10001110011010110011100011000110001",
            ['O']="01110100011000110001100011000101110", ['P']="11110100011000111110100001000010000",
            ['Q']="01110100011000110001101011001001101", ['R']="11110100011000111110101001001010001",
            ['S']="01111100001000001110000010000111110", ['T']="11111001000010000100001000010000100",
            ['U']="10001100011000110001100011000101110", ['V']="10001100011000110001100010101000100",
            ['W']="10001100011000110101101011101110001", ['X']="10001100010101000100010101000110001",
            ['Y']="10001100010101000100001000010000100", ['Z']="11111000010001000100010001000011111",
            ['0']="01110100011001110101110011000101110", ['1']="00100011000010000100001000010001110",
            ['2']="01110100010000100010001000100011111", ['3']="11110000010000101110000010000111110",
            ['4']="00010001100101010010111110001000010", ['5']="11111100001000011110000010000111110",
            ['6']="01110100001000011110100011000101110", ['7']="11111000010001000100010000100001000",
            ['8']="01110100011000101110100011000101110", ['9']="01110100011000101111000010000101110",
            ['.']="00000000000000000000000000011000110", [',']="00000000000000000000001100011000100",
            [':']="00000001100011000000001100011000000", ['-']="00000000000000011111000000000000000",
            ['_']="00000000000000000000000000000011111", ['!']="00100001000010000100001000000000100",
            ['?']="01110100010000100010001000000000100", ['/']="00001000100001000100010001000010000",
            ['(']="00010001000100001000010000010000010", [')']="01000001000010000010001000100001000"
        };

        public static string Pattern(char value) => Glyphs.TryGetValue(char.ToUpperInvariant(value), out var pattern)
            ? pattern
            : Missing;
    }
}
