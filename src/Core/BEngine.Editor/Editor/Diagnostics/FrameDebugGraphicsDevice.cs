using BEngine.Rendering.Rhi;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Diagnostics;

public enum FrameDebugEventKind
{
    Clear,
    Draw
}

public readonly record struct FrameDebugTextureBinding(
    int Slot,
    string Label,
    int Width = 0,
    int Height = 0,
    GraphicsTextureFormat Format = default,
    GraphicsTextureUsage Usage = default,
    GraphicsTextureFilter MinFilter = default,
    GraphicsTextureFilter MagFilter = default,
    GraphicsTextureAddressMode AddressMode = default);

public readonly record struct FrameDebugIntProperty(string Name, int Value);
public readonly record struct FrameDebugFloatProperty(string Name, float Value);
public readonly record struct FrameDebugVectorProperty(string Name, NVector4 Value);
public readonly record struct FrameDebugMatrixProperty(string Name, NMatrix4x4 Value);

public readonly record struct FrameDebugShaderStage(
    GraphicsShaderStage Stage,
    GraphicsShaderLanguage Language,
    string EntryPoint);

public sealed record FrameDebugProgramState(
    string Label,
    IReadOnlyList<FrameDebugShaderStage> Stages);

public readonly record struct FrameDebugRenderTargetState(
    string Label,
    int Width,
    int Height,
    GraphicsTextureFormat? ColorFormat,
    GraphicsTextureFormat? DepthFormat,
    bool ColorSampled,
    bool DepthSampled);

public sealed record FrameDebugMeshState(
    string Label,
    int AvailableVertexCount,
    GraphicsBufferUsage Usage,
    int StrideBytes,
    IReadOnlyList<GraphicsVertexAttribute> Attributes);

public readonly record struct FrameDebugMarker(
    string Group = "",
    string BatchName = "",
    Guid? Material = null,
    string Shader = "",
    string Atlas = "",
    string SourceName = "",
    int? SourceInstanceId = null);

public sealed record FrameDebugRenderState(
    GraphicsRect Viewport,
    GraphicsRect? Scissor,
    GraphicsDepthState DepthState,
    GraphicsBlendMode BlendMode,
    GraphicsRasterizerState RasterizerState,
    string ProgramLabel,
    string RenderTargetLabel,
    IReadOnlyList<FrameDebugTextureBinding> Textures,
    IReadOnlyList<FrameDebugIntProperty>? Ints = null,
    IReadOnlyList<FrameDebugFloatProperty>? Floats = null,
    IReadOnlyList<FrameDebugVectorProperty>? Vectors = null,
    IReadOnlyList<FrameDebugMatrixProperty>? Matrices = null,
    FrameDebugProgramState? Program = null,
    FrameDebugRenderTargetState? RenderTarget = null);

public sealed record FrameDebugEvent(
    int Index,
    FrameDebugEventKind Kind,
    string Name,
    FrameDebugRenderState State,
    string MeshLabel,
    GraphicsPrimitiveTopology Topology,
    int FirstVertex,
    int VertexCount,
    int TriangleCount,
    int LineCount,
    GraphicsClearFlags ClearFlags,
    NVector4 ClearColor,
    bool Executed,
    string Error,
    FrameDebugMarker Marker,
    FrameDebugMeshState? Mesh = null);

public sealed record FrameDebugCaptureSnapshot(
    long CaptureId,
    string TargetName,
    GraphicsBackend Backend,
    DateTimeOffset CapturedAt,
    IReadOnlyList<FrameDebugEvent> Events);

public readonly record struct FrameDebugPreviewArea(
    GraphicsRect Region,
    int SurfaceWidth,
    int SurfaceHeight)
{
    public bool IsValid => Region.Width > 0 && Region.Height > 0 &&
                           SurfaceWidth > 0 && SurfaceHeight > 0;
}

/// <summary>
/// Editor-only graphics-device decorator used to record pixel-writing operations and capture one
/// selected intermediate output without truncating the frame presented by the Game view.
/// Resource wrappers preserve the invariant that resources report this decorator as their owner.
/// </summary>
public sealed class FrameDebugGraphicsDevice : IGraphicsDevice, IGraphicsDeviceStatistics,
    IGraphicsResourceRetirement, IGraphicsDebugAnnotations, IGraphicsColorReadback
{
    private const int TextureSlotCount = 32;
    private static readonly IReadOnlyList<FrameDebugTextureBinding> EmptyTextureBindings =
        Array.AsReadOnly(Array.Empty<FrameDebugTextureBinding>());

    private readonly IGraphicsDevice _inner;
    private readonly bool _ownsDevice;
    private readonly IGraphicsTexture2D?[] _boundTextures = new IGraphicsTexture2D?[TextureSlotCount];
    private List<FrameDebugEvent>? _captureBuffer;
    private List<FrameDebugMarker>? _markerStack;
    private GraphicsColorReadbackRequest? _previewRequest;
    private DebugProgram? _activeProgram;
    private GraphicsRect _viewport;
    private GraphicsRect? _scissor;
    private GraphicsDepthState _depthState = GraphicsDepthState.Default;
    private GraphicsBlendMode _blendMode;
    private GraphicsRasterizerState _rasterizerState = GraphicsRasterizerState.Default;
    private GraphicsDrawStatistics _drawStatistics;
    private FrameDebugRenderTargetState _renderTarget = MainRenderTarget(default);
    private int _operationIndex;
    private int _previewEventCount = -1;
    private FrameDebugPreviewArea _previewArea;
    private int _frameToken;
    private bool _captureEvents;
    private bool _frameActive;
    private bool _disposed;

    public FrameDebugGraphicsDevice(IGraphicsDevice inner, bool ownsDevice = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ownsDevice = ownsDevice;
    }

    public static FrameDebugGraphicsDevice Wrap(IGraphicsDevice device, bool ownsDevice = false) =>
        device as FrameDebugGraphicsDevice ?? new FrameDebugGraphicsDevice(device, ownsDevice);

    public IGraphicsDevice InnerDevice => _inner;
    public GraphicsBackend Backend => _inner.Backend;
    public GraphicsDeviceCapabilities Capabilities => _inner.Capabilities;
    public GraphicsDrawStatistics DrawStatistics => _drawStatistics;
    public bool DebugMarkersEnabled => _frameActive && _captureEvents;
    public GraphicsColorReadbackCapabilities ColorReadbackCapabilities =>
        _inner is IGraphicsColorReadback readback
            ? readback.ColorReadbackCapabilities
            : GraphicsColorReadbackCapabilities.Unavailable(
                $"{_inner.GetType().Name} does not support color readback.");

    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description)
    {
        ThrowIfDisposed();
        return new DebugProgram(this, _inner.CreateProgram(description), description);
    }

    public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
    {
        ThrowIfDisposed();
        return new DebugMesh(this, _inner.CreateMesh(description));
    }

    public IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default)
    {
        ThrowIfDisposed();
        return new DebugTexture2D(this, _inner.CreateTexture2D(label, description, initialData));
    }

    public IGraphicsRenderTarget CreateRenderTarget(
        string label,
        GraphicsRenderTargetDescription description)
    {
        ThrowIfDisposed();
        return new DebugRenderTarget(this, _inner.CreateRenderTarget(label, description));
    }

    public GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region,
        int surfaceWidth,
        int surfaceHeight)
    {
        ThrowIfDisposed();
        return _inner is IGraphicsColorReadback readback
            ? readback.RequestColorReadback(region, surfaceWidth, surfaceHeight)
            : GraphicsColorReadbackRequest.Unavailable(region,
                $"{_inner.GetType().Name} does not support color readback.");
    }

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(renderTarget);
        var label = renderTarget.Label;
        var targetState = CaptureRenderTarget(renderTarget, label);
        var innerTarget = UnwrapRenderTarget(renderTarget);
        var scope = _inner.PushRenderTarget(innerTarget);
        var previous = _renderTarget;
        _renderTarget = targetState;
        return new RenderTargetScope(this, scope, previous);
    }

    public void SetViewport(GraphicsRect viewport)
    {
        ThrowIfDisposed();
        _inner.SetViewport(viewport);
        _viewport = viewport;
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        ThrowIfDisposed();
        _inner.SetScissor(scissor);
        _scissor = scissor;
    }

    public void Clear(GraphicsClearFlags flags, NVector4 color)
    {
        ThrowIfDisposed();
        ExecutePixelOperation(FrameDebugEventKind.Clear, flags, color, null, 0, 0,
            GraphicsPrimitiveTopology.TriangleList, static (device, state) =>
                device._inner.Clear(state.ClearFlags, state.ClearColor));
    }

    public void SetDepthState(GraphicsDepthState state)
    {
        ThrowIfDisposed();
        _inner.SetDepthState(state);
        _depthState = state;
    }

    public void SetBlendMode(GraphicsBlendMode mode)
    {
        ThrowIfDisposed();
        _inner.SetBlendMode(mode);
        _blendMode = mode;
    }

    public void SetRasterizerState(GraphicsRasterizerState state)
    {
        ThrowIfDisposed();
        _inner.SetRasterizerState(state);
        _rasterizerState = state;
    }

    public void BindTexture(int slot, IGraphicsTexture2D texture)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        var innerTexture = UnwrapTexture(texture);
        _inner.BindTexture(slot, innerTexture);
        if ((uint)slot < TextureSlotCount) _boundTextures[slot] = texture;
    }

    public void Draw(IGraphicsMesh mesh)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mesh);
        Draw(mesh, mesh.VertexCount, 0);
    }

    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mesh);
        var innerMesh = UnwrapMesh(mesh);
        ExecutePixelOperation(FrameDebugEventKind.Draw, GraphicsClearFlags.None, default,
            mesh, vertexCount, firstVertex, mesh.Topology,
            static (device, state) => device._inner.Draw(state.Mesh!, state.VertexCount, state.FirstVertex),
            innerMesh);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        ThrowIfDisposed();
        ExecutePixelOperation(FrameDebugEventKind.Draw, GraphicsClearFlags.None, default,
            null, vertexCount, firstVertex, topology,
            static (device, state) =>
                device._inner.Draw(state.VertexCount, state.Topology, state.FirstVertex));
    }

    public void RetireResource(IDisposable resource)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is DebugResource foreign && !ReferenceEquals(foreign.Owner, this))
            throw IncompatibleResource(foreign);
        if (_inner is not IGraphicsResourceRetirement retirement)
        {
            resource.Dispose();
            return;
        }

        if (resource is DebugResource wrapped && ReferenceEquals(wrapped.Owner, this))
        {
            ForgetResource(wrapped);
            retirement.RetireResource(wrapped.DetachForRetirement());
            return;
        }
        retirement.RetireResource(resource);
    }

    public MarkerScope PushMarker(FrameDebugMarker marker)
    {
        if (!_frameActive || !_captureEvents) return default;
        _markerStack ??= [];
        _markerStack.Add(marker);
        return new MarkerScope(this, _frameToken, _markerStack.Count);
    }

    public void PushDebugMarker(GraphicsDebugMarker marker)
    {
        if (!DebugMarkersEnabled) return;
        _markerStack ??= [];
        _markerStack.Add(new FrameDebugMarker(
            marker.Group,
            marker.BatchName,
            marker.Material,
            marker.Shader,
            marker.Atlas,
            marker.SourceName,
            marker.SourceInstanceId));
    }

    public void PopDebugMarker()
    {
        if (!DebugMarkersEnabled || _markerStack is not { Count: > 0 }) return;
        _markerStack.RemoveAt(_markerStack.Count - 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _frameActive = false;
        _captureEvents = false;
        _captureBuffer?.Clear();
        _markerStack?.Clear();
        _previewRequest?.Dispose();
        _previewRequest = null;
        Array.Clear(_boundTextures);
        _activeProgram = null;
        if (_ownsDevice) _inner.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal DeviceFrameScope BeginDebugFrame(
        bool captureEvents,
        int previewEventCount,
        FrameDebugPreviewArea previewArea)
    {
        ThrowIfDisposed();
        if (_frameActive) throw new InvalidOperationException("A frame-debug scope is already active.");
        if (previewEventCount < -1) throw new ArgumentOutOfRangeException(nameof(previewEventCount));
        _frameToken = unchecked(_frameToken + 1);
        if (_frameToken == 0) _frameToken = 1;
        _frameActive = true;
        _captureEvents = captureEvents;
        _previewEventCount = previewEventCount;
        _previewArea = previewArea;
        _renderTarget = MainRenderTarget(previewArea);
        _previewRequest?.Dispose();
        _previewRequest = null;
        _operationIndex = 0;
        if (captureEvents)
        {
            _captureBuffer ??= [];
            _captureBuffer.Clear();
            _markerStack?.Clear();
        }
        if (previewEventCount == 0) CapturePreview();
        return new DeviceFrameScope(this, _frameToken, captureEvents);
    }

    private void ExecutePixelOperation(
        FrameDebugEventKind kind,
        GraphicsClearFlags clearFlags,
        NVector4 clearColor,
        IGraphicsMesh? displayMesh,
        int vertexCount,
        int firstVertex,
        GraphicsPrimitiveTopology topology,
        Action<FrameDebugGraphicsDevice, DrawExecutionState> execute,
        IGraphicsMesh? executionMesh = null)
    {
        var index = _frameActive ? _operationIndex++ : -1;
        var shouldCapture = _frameActive && _captureEvents;
        var error = string.Empty;
        var executed = false;
        try
        {
            execute(this, new DrawExecutionState(
                executionMesh, vertexCount, firstVertex, topology, clearFlags, clearColor));
            if (kind == FrameDebugEventKind.Draw)
                _drawStatistics = _drawStatistics.AddDraw(vertexCount, topology);
            executed = true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            throw;
        }
        finally
        {
            if (shouldCapture)
                RecordEvent(index, kind, clearFlags, clearColor, displayMesh,
                    vertexCount, firstVertex, topology, executed, error);
            if (_frameActive && _previewEventCount == index + 1) CapturePreview();
        }
    }

    private void RecordEvent(
        int index,
        FrameDebugEventKind kind,
        GraphicsClearFlags clearFlags,
        NVector4 clearColor,
        IGraphicsMesh? mesh,
        int vertexCount,
        int firstVertex,
        GraphicsPrimitiveTopology topology,
        bool executed,
        string error)
    {
        var marker = CurrentMarker();
        var meshLabel = mesh?.Label ?? string.Empty;
        var name = ResolveEventName(kind, meshLabel, marker);
        _captureBuffer!.Add(new FrameDebugEvent(
            index,
            kind,
            name,
            CaptureState(),
            meshLabel,
            topology,
            firstVertex,
            vertexCount,
            topology == GraphicsPrimitiveTopology.TriangleList ? vertexCount / 3 : 0,
            topology == GraphicsPrimitiveTopology.LineList ? vertexCount / 2 : 0,
            clearFlags,
            clearColor,
            executed,
            error,
            marker,
            CaptureMesh(mesh)));
    }

    private FrameDebugRenderState CaptureState()
    {
        var count = 0;
        for (var slot = 0; slot < _boundTextures.Length; slot++)
            if (_boundTextures[slot] is not null) count++;
        if (count == 0)
            return CreateRenderState(EmptyTextureBindings);

        var textures = new FrameDebugTextureBinding[count];
        var output = 0;
        for (var slot = 0; slot < _boundTextures.Length; slot++)
        {
            if (_boundTextures[slot] is not { } texture) continue;
            var description = texture.Description;
            textures[output++] = new FrameDebugTextureBinding(
                slot,
                texture.Label,
                description.Width,
                description.Height,
                description.Format,
                description.Usage,
                description.MinFilter,
                description.MagFilter,
                description.AddressMode);
        }
        return CreateRenderState(Array.AsReadOnly(textures));
    }

    private FrameDebugRenderState CreateRenderState(
        IReadOnlyList<FrameDebugTextureBinding> textures) =>
        new(
            _viewport,
            _scissor,
            _depthState,
            _blendMode,
            _rasterizerState,
            _activeProgram?.Label ?? string.Empty,
            _renderTarget.Label,
            textures,
            _activeProgram?.CaptureInts(),
            _activeProgram?.CaptureFloats(),
            _activeProgram?.CaptureVectors(),
            _activeProgram?.CaptureMatrices(),
            _activeProgram?.CaptureState(),
            _renderTarget);

    private static FrameDebugMeshState? CaptureMesh(IGraphicsMesh? mesh)
    {
        if (mesh is null) return null;
        var attributes = Array.AsReadOnly(mesh.Layout.Attributes.ToArray());
        return new FrameDebugMeshState(mesh.Label, mesh.VertexCount, mesh.Usage,
            mesh.Layout.StrideBytes, attributes);
    }

    private static FrameDebugRenderTargetState CaptureRenderTarget(
        IGraphicsRenderTarget renderTarget,
        string label)
    {
        var color = renderTarget.ColorTexture?.Description;
        var depth = renderTarget.DepthTexture?.Description;
        return new FrameDebugRenderTargetState(
            label,
            renderTarget.Width,
            renderTarget.Height,
            color?.Format,
            depth?.Format,
            color?.Usage.HasFlag(GraphicsTextureUsage.Sampled) == true,
            depth?.Usage.HasFlag(GraphicsTextureUsage.Sampled) == true);
    }

    private static FrameDebugRenderTargetState MainRenderTarget(FrameDebugPreviewArea area) =>
        new("Main", area.SurfaceWidth, area.SurfaceHeight, null, null, false, false);

    private FrameDebugMarker CurrentMarker()
    {
        if (_markerStack is not { Count: > 0 }) return default;
        var result = default(FrameDebugMarker);
        foreach (var marker in _markerStack)
            result = new FrameDebugMarker(
                string.IsNullOrWhiteSpace(marker.Group) ? result.Group : marker.Group,
                string.IsNullOrWhiteSpace(marker.BatchName) ? result.BatchName : marker.BatchName,
                marker.Material ?? result.Material,
                string.IsNullOrWhiteSpace(marker.Shader) ? result.Shader : marker.Shader,
                string.IsNullOrWhiteSpace(marker.Atlas) ? result.Atlas : marker.Atlas,
                string.IsNullOrWhiteSpace(marker.SourceName) ? result.SourceName : marker.SourceName,
                marker.SourceInstanceId ?? result.SourceInstanceId);
        return result;
    }

    private static string ResolveEventName(
        FrameDebugEventKind kind,
        string meshLabel,
        FrameDebugMarker marker)
    {
        if (!string.IsNullOrWhiteSpace(marker.BatchName)) return marker.BatchName;
        if (!string.IsNullOrWhiteSpace(marker.Group)) return marker.Group;
        if (kind == FrameDebugEventKind.Clear) return "Clear";
        return string.IsNullOrWhiteSpace(meshLabel) ? "Draw" : $"Draw {meshLabel}";
    }

    private DeviceFrameResult EndDebugFrame(int token, bool captured)
    {
        if (!_frameActive || token != _frameToken) return default;
        if (_previewRequest is null && _previewEventCount < 0) CapturePreview();
        _frameActive = false;
        _captureEvents = false;
        _previewEventCount = -1;
        _operationIndex = 0;
        _markerStack?.Clear();
        var request = _previewRequest;
        _previewRequest = null;
        var events = captured && _captureBuffer is { Count: > 0 }
            ? _captureBuffer.ToArray()
            : [];
        return new DeviceFrameResult(events, request);
    }

    private void CapturePreview()
    {
        if (_previewRequest is not null) return;
        _previewRequest = _previewArea.IsValid
            ? RequestColorReadback(
                _previewArea.Region,
                _previewArea.SurfaceWidth,
                _previewArea.SurfaceHeight)
            : GraphicsColorReadbackRequest.Unavailable(
                _previewArea.Region,
                "The render loop did not provide a valid top-left preview area.");
    }

    private void PopMarker(int token, int expectedDepth)
    {
        if (!_frameActive || token != _frameToken || _markerStack is null ||
            _markerStack.Count != expectedDepth) return;
        _markerStack.RemoveAt(_markerStack.Count - 1);
    }

    private void BindProgram(DebugProgram program)
    {
        _activeProgram = program;
    }

    private void RestoreRenderTarget(FrameDebugRenderTargetState state)
    {
        _renderTarget = state;
    }

    private void ForgetResource(DebugResource resource)
    {
        if (ReferenceEquals(_activeProgram, resource)) _activeProgram = null;
        if (resource is not DebugTexture2D texture) return;
        for (var index = 0; index < _boundTextures.Length; index++)
            if (ReferenceEquals(_boundTextures[index], texture)) _boundTextures[index] = null;
    }

    private IGraphicsMesh UnwrapMesh(IGraphicsMesh mesh) => mesh switch
    {
        DebugMesh wrapped when ReferenceEquals(wrapped.Owner, this) => wrapped.InnerMesh,
        DebugResource wrapped => throw IncompatibleResource(wrapped),
        _ => mesh
    };

    private IGraphicsTexture2D UnwrapTexture(IGraphicsTexture2D texture) => texture switch
    {
        DebugTexture2D wrapped when ReferenceEquals(wrapped.Owner, this) => wrapped.InnerTexture,
        DebugResource wrapped => throw IncompatibleResource(wrapped),
        _ => texture
    };

    private IGraphicsRenderTarget UnwrapRenderTarget(IGraphicsRenderTarget target) => target switch
    {
        DebugRenderTarget wrapped when ReferenceEquals(wrapped.Owner, this) => wrapped.InnerRenderTarget,
        DebugResource wrapped => throw IncompatibleResource(wrapped),
        _ => target
    };

    private static ArgumentException IncompatibleResource(DebugResource resource) => new(
        $"Graphics resource '{resource.Label}' belongs to another frame-debug device.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal readonly record struct DrawExecutionState(
        IGraphicsMesh? Mesh,
        int VertexCount,
        int FirstVertex,
        GraphicsPrimitiveTopology Topology,
        GraphicsClearFlags ClearFlags,
        NVector4 ClearColor);

    internal readonly record struct DeviceFrameResult(
        FrameDebugEvent[] Events,
        GraphicsColorReadbackRequest? PreviewRequest);

    public struct MarkerScope : IDisposable
    {
        private FrameDebugGraphicsDevice? _owner;
        private readonly int _token;
        private readonly int _depth;

        internal MarkerScope(FrameDebugGraphicsDevice owner, int token, int depth)
        {
            _owner = owner;
            _token = token;
            _depth = depth;
        }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.PopMarker(_token, _depth);
        }
    }

    internal struct DeviceFrameScope : IDisposable
    {
        private FrameDebugGraphicsDevice? _owner;
        private readonly int _token;
        private readonly bool _captured;

        internal DeviceFrameScope(FrameDebugGraphicsDevice owner, int token, bool captured)
        {
            _owner = owner;
            _token = token;
            _captured = captured;
        }

        internal DeviceFrameResult Complete()
        {
            var owner = _owner;
            _owner = null;
            return owner?.EndDebugFrame(_token, _captured) ?? default;
        }

        public void Dispose() => Complete();
    }

    private abstract class DebugResource : IGraphicsResource
    {
        private IGraphicsResource? _innerResource;
        private readonly bool _ownsResource;

        protected DebugResource(
            FrameDebugGraphicsDevice owner,
            IGraphicsResource innerResource,
            bool ownsResource = true)
        {
            Owner = owner;
            _innerResource = innerResource;
            _ownsResource = ownsResource;
        }

        internal FrameDebugGraphicsDevice Owner { get; }
        protected IGraphicsResource InnerResource => _innerResource ??
            throw new ObjectDisposedException(GetType().Name);
        public IGraphicsDevice Device => Owner;
        public string Label => InnerResource.Label;

        public void Dispose()
        {
            var resource = Interlocked.Exchange(ref _innerResource, null);
            if (resource is null) return;
            Owner.ForgetResource(this);
            if (_ownsResource) resource.Dispose();
        }

        internal IDisposable DetachForRetirement()
        {
            var resource = Interlocked.Exchange(ref _innerResource, null) ??
                throw new ObjectDisposedException(GetType().Name);
            return resource;
        }
    }

    private sealed class DebugProgram : DebugResource, IGraphicsProgram
    {
        private readonly FrameDebugProgramState _state;
        private Dictionary<string, int>? _ints;
        private Dictionary<string, float>? _floats;
        private Dictionary<string, NVector4>? _vectors;
        private Dictionary<string, NMatrix4x4>? _matrices;

        internal DebugProgram(
            FrameDebugGraphicsDevice owner,
            IGraphicsProgram inner,
            GraphicsShaderProgramDescription description)
            : base(owner, inner)
        {
            _state = new FrameDebugProgramState(description.Label, Array.AsReadOnly(
            [
                new FrameDebugShaderStage(description.VertexShader.Stage,
                    description.VertexShader.Language, description.VertexShader.EntryPoint),
                new FrameDebugShaderStage(description.FragmentShader.Stage,
                    description.FragmentShader.Language, description.FragmentShader.EntryPoint)
            ]));
        }

        private IGraphicsProgram InnerProgram => (IGraphicsProgram)InnerResource;

        public void Bind()
        {
            InnerProgram.Bind();
            Owner.BindProgram(this);
        }

        public void SetMatrix4x4(string name, NMatrix4x4 value)
        {
            InnerProgram.SetMatrix4x4(name, value);
            (_matrices ??= new Dictionary<string, NMatrix4x4>(StringComparer.Ordinal))[name] = value;
        }

        public void SetVector4(string name, NVector4 value)
        {
            InnerProgram.SetVector4(name, value);
            (_vectors ??= new Dictionary<string, NVector4>(StringComparer.Ordinal))[name] = value;
        }

        public void SetFloat(string name, float value)
        {
            InnerProgram.SetFloat(name, value);
            (_floats ??= new Dictionary<string, float>(StringComparer.Ordinal))[name] = value;
        }

        public void SetInt(string name, int value)
        {
            InnerProgram.SetInt(name, value);
            (_ints ??= new Dictionary<string, int>(StringComparer.Ordinal))[name] = value;
        }

        internal IReadOnlyList<FrameDebugIntProperty> CaptureInts() =>
            CaptureProperties(_ints, static pair => new FrameDebugIntProperty(pair.Key, pair.Value));

        internal IReadOnlyList<FrameDebugFloatProperty> CaptureFloats() =>
            CaptureProperties(_floats, static pair => new FrameDebugFloatProperty(pair.Key, pair.Value));

        internal IReadOnlyList<FrameDebugVectorProperty> CaptureVectors() =>
            CaptureProperties(_vectors, static pair => new FrameDebugVectorProperty(pair.Key, pair.Value));

        internal IReadOnlyList<FrameDebugMatrixProperty> CaptureMatrices() =>
            CaptureProperties(_matrices, static pair => new FrameDebugMatrixProperty(pair.Key, pair.Value));

        internal FrameDebugProgramState CaptureState() => _state;

        private static IReadOnlyList<TProperty> CaptureProperties<TValue, TProperty>(
            Dictionary<string, TValue>? source,
            Func<KeyValuePair<string, TValue>, TProperty> selector)
        {
            if (source is null || source.Count == 0) return Array.Empty<TProperty>();
            return Array.AsReadOnly(source.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(selector).ToArray());
        }
    }

    private sealed class DebugMesh : DebugResource, IGraphicsMesh
    {
        internal DebugMesh(FrameDebugGraphicsDevice owner, IGraphicsMesh inner)
            : base(owner, inner)
        {
        }

        internal IGraphicsMesh InnerMesh => (IGraphicsMesh)InnerResource;
        public GraphicsVertexLayout Layout => InnerMesh.Layout;
        public GraphicsPrimitiveTopology Topology => InnerMesh.Topology;
        public GraphicsBufferUsage Usage => InnerMesh.Usage;
        public int VertexCount => InnerMesh.VertexCount;
        public void Update(ReadOnlySpan<float> vertices) => InnerMesh.Update(vertices);
    }

    private sealed class DebugTexture2D : DebugResource, IGraphicsTexture2D
    {
        internal DebugTexture2D(
            FrameDebugGraphicsDevice owner,
            IGraphicsTexture2D inner,
            bool ownsResource = true)
            : base(owner, inner, ownsResource)
        {
        }

        internal IGraphicsTexture2D InnerTexture => (IGraphicsTexture2D)InnerResource;
        public GraphicsTextureDescription Description => InnerTexture.Description;
        public void Update(ReadOnlySpan<byte> pixels) => InnerTexture.Update(pixels);
    }

    private sealed class DebugRenderTarget : DebugResource, IGraphicsRenderTarget
    {
        private DebugTexture2D? _colorTexture;
        private DebugTexture2D? _depthTexture;

        internal DebugRenderTarget(FrameDebugGraphicsDevice owner, IGraphicsRenderTarget inner)
            : base(owner, inner)
        {
        }

        internal IGraphicsRenderTarget InnerRenderTarget => (IGraphicsRenderTarget)InnerResource;
        public int Width => InnerRenderTarget.Width;
        public int Height => InnerRenderTarget.Height;
        public IGraphicsTexture2D? ColorTexture => WrapAttachment(
            InnerRenderTarget.ColorTexture, ref _colorTexture);
        public IGraphicsTexture2D? DepthTexture => WrapAttachment(
            InnerRenderTarget.DepthTexture, ref _depthTexture);

        private IGraphicsTexture2D? WrapAttachment(
            IGraphicsTexture2D? texture,
            ref DebugTexture2D? wrapped)
        {
            if (texture is null) return null;
            return wrapped ??= new DebugTexture2D(Owner, texture, ownsResource: false);
        }
    }

    private sealed class RenderTargetScope(
        FrameDebugGraphicsDevice owner,
        IDisposable innerScope,
        FrameDebugRenderTargetState previousState) : IDisposable
    {
        private IDisposable? _innerScope = innerScope;

        public void Dispose()
        {
            var scope = Interlocked.Exchange(ref _innerScope, null);
            if (scope is null) return;
            try { scope.Dispose(); }
            finally { owner.RestoreRenderTarget(previousState); }
        }
    }

}
