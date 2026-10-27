using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine.Rendering.Rhi;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Platform;

/// <summary>WebGL 2 implementation used by the browser Player Host.</summary>
public sealed partial class WebGlGraphicsDevice : IGraphicsPresentationDevice, IGraphicsDeviceStatistics,
    IGraphicsTextureFormatSupport, IGraphicsColorReadback, IGraphicsStateCache, IGraphicsEncodedImagePreparation
{
    private const GraphicsDeviceFeatures Features =
        GraphicsDeviceFeatures.Rasterization |
        GraphicsDeviceFeatures.ShaderPrograms |
        GraphicsDeviceFeatures.StaticVertexBuffers |
        GraphicsDeviceFeatures.DynamicVertexBuffers |
        GraphicsDeviceFeatures.SampledTextures |
        GraphicsDeviceFeatures.RenderTargets |
        GraphicsDeviceFeatures.DepthTextures |
        GraphicsDeviceFeatures.AlphaBlending |
        GraphicsDeviceFeatures.ScissorRectangles |
        GraphicsDeviceFeatures.DepthBias;

    private GraphicsDrawStatistics _drawStatistics;
    private GraphicsRect _viewport;
    private bool _disposed;
    private int _boundProgram = -1;
    private readonly int[] _boundTextures = new int[32];
    private long _stateGeneration;
    private long _programBinds, _uniformWrites, _textureBinds, _vertexArrayBinds, _cacheHits;
    private bool _viewportKnown, _scissorKnown;
    private GraphicsRect? _scissor;
    private GraphicsDepthState? _depthState;
    private GraphicsBlendMode? _blendMode;
    private GraphicsRasterizerState? _rasterizerState;
    private int _frameWidth, _frameHeight;

    public WebGlGraphicsDevice(string canvasSelector = "#bengine-game")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasSelector);
        ResetState();
        var information = JsonSerializer.Deserialize(
                              WebGlInterop.Initialize(canvasSelector),
                              WebGlJsonContext.Default.WebGlDeviceInformation) ??
            throw new InvalidOperationException("WebGL did not return device information.");
        Capabilities = new GraphicsDeviceCapabilities(
            GraphicsBackend.WebGL,
            information.Renderer,
            information.Version,
            Features,
            [GraphicsShaderLanguage.Glsl])
        {
            DeviceVendor = information.Vendor,
            MaxTextureSize = information.MaxTextureSize
        };
        GraphicsUtility.Current.ReportActive(Capabilities);
    }

    public GraphicsBackend Backend => GraphicsBackend.WebGL;
    public GraphicsDeviceCapabilities Capabilities { get; }
    public GraphicsDrawStatistics DrawStatistics => _drawStatistics;
    public GraphicsColorReadbackCapabilities ColorReadbackCapabilities => new(true, 64 * 1024 * 1024);
    public GraphicsBindingStatistics BindingStatistics =>
        new(_programBinds, _uniformWrites, _textureBinds, _vertexArrayBinds, _cacheHits);

    public void InvalidateState()
    {
        ThrowIfDisposed();
        ResetState();
    }

    private void ResetState()
    {
        _boundProgram = -1;
        InvalidateTextureBindings();
        _viewportKnown = _scissorKnown = false;
        _depthState = null; _blendMode = null; _rasterizerState = null;
        _stateGeneration++;
    }

    // Texture creation and uploads bind/unbind the current JS texture unit.
    // Forget all units rather than querying GL or assuming which unit was active.
    private void InvalidateTextureBindings() => Array.Fill(_boundTextures, -1);

    private void UseProgram(int handle)
    {
        if (_boundProgram == handle) { _cacheHits++; return; }
        WebGlInterop.BindProgram(handle);
        _boundProgram = handle; _programBinds++;
    }

    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        description.Validate();
        if (description.VertexShader.Language != GraphicsShaderLanguage.Glsl)
            throw new NotSupportedException("WebGL accepts GLSL shader source.");
        return new WebGlProgram(this, description.Label,
            NormalizeShader(description.VertexShader.Code), NormalizeShader(description.FragmentShader.Code));
    }

    public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        return new WebGlMesh(this, description);
    }

    public bool SupportsTextureFormat(GraphicsTextureFormat format) => format is
        GraphicsTextureFormat.R8Unorm or GraphicsTextureFormat.Rgba8Unorm or
        GraphicsTextureFormat.Depth24Unorm or GraphicsTextureFormat.Depth24Stencil8;

    public IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        if (!SupportsTextureFormat(description.Format))
            throw new NotSupportedException($"WebGL 2 does not support {description.Format} in BEngine.");
        try { return new WebGlTexture(this, label, description, initialData, ownsHandle: true); }
        finally { InvalidateTextureBindings(); }
    }

    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        try { return new WebGlRenderTarget(this, label, description); }
        finally { InvalidateTextureBindings(); }
    }

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
    {
        ThrowIfDisposed();
        var target = Require<WebGlRenderTarget>(renderTarget);
        return new FramebufferScope(this, WebGlInterop.BindRenderTarget(target.Handle));
    }

    public void SetViewport(GraphicsRect viewport)
    {
        ThrowIfDisposed();
        viewport.Validate();
        if (_viewportKnown && _viewport == viewport) { _cacheHits++; return; }
        _viewport = viewport;
        WebGlInterop.SetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        _viewportKnown = true;
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        ThrowIfDisposed();
        GraphicsRect? deviceScissor = null;
        if (scissor is { } rect)
        {
            rect.Validate();
            deviceScissor = new GraphicsRect(_viewport.X + rect.X,
                _viewport.Y + _viewport.Height - rect.Y - rect.Height, rect.Width, rect.Height);
        }
        if (_scissorKnown && _scissor == deviceScissor) { _cacheHits++; return; }
        if (deviceScissor is null)
        {
            WebGlInterop.DisableScissor();
        }
        else
        {
            var converted = deviceScissor.Value;
            WebGlInterop.SetScissor(converted.X, converted.Y, converted.Width, converted.Height);
        }
        _scissor = deviceScissor; _scissorKnown = true;
    }

    public void Clear(GraphicsClearFlags flags, NumericsVector4 color)
    {
        ThrowIfDisposed();
        WebGlInterop.Clear((int)flags, color.X, color.Y, color.Z, color.W);
    }

    public void SetDepthState(GraphicsDepthState state)
    {
        ThrowIfDisposed();
        if (_depthState == state) { _cacheHits++; return; }
        WebGlInterop.SetDepthState(state.TestEnabled, state.WriteEnabled);
        _depthState = state;
    }

    public void SetBlendMode(GraphicsBlendMode mode)
    {
        ThrowIfDisposed();
        if (_blendMode == mode) { _cacheHits++; return; }
        WebGlInterop.SetBlendMode((int)mode);
        _blendMode = mode;
    }

    public void SetRasterizerState(GraphicsRasterizerState state)
    {
        ThrowIfDisposed();
        if (_rasterizerState is { } cached && cached.DepthBiasEnabled == state.DepthBiasEnabled &&
            cached.CullMode == state.CullMode && SameBits(cached.SlopeScale, state.SlopeScale) &&
            SameBits(cached.ConstantBias, state.ConstantBias)) { _cacheHits++; return; }
        WebGlInterop.SetRasterizerState(
            state.DepthBiasEnabled, state.SlopeScale, state.ConstantBias, (int)state.CullMode);
        _rasterizerState = state;
    }

    public void BindTexture(int slot, IGraphicsTexture2D texture)
    {
        ThrowIfDisposed();
        if (slot is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(slot));
        var typed = Require<WebGlTexture>(texture);
        if ((typed.Description.Usage & GraphicsTextureUsage.Sampled) == 0)
            throw new InvalidOperationException($"Texture '{texture.Label}' was not created for sampling.");
        if (_boundTextures[slot] == typed.Handle) { _cacheHits++; return; }
        WebGlInterop.BindTexture(slot, typed.Handle);
        _boundTextures[slot] = typed.Handle; _textureBinds++;
    }

    public void Draw(IGraphicsMesh mesh)
    {
        var typed = Require<WebGlMesh>(mesh);
        Draw(typed, typed.VertexCount, 0);
    }

    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
    {
        var typed = Require<WebGlMesh>(mesh);
        if (vertexCount < 0 || firstVertex < 0 || firstVertex > typed.VertexCount ||
            vertexCount > typed.VertexCount - firstVertex)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        WebGlInterop.DrawMesh(typed.Handle, (int)typed.Topology, firstVertex, vertexCount);
        _vertexArrayBinds++;
        RecordDraw(vertexCount, typed.Topology);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        ThrowIfDisposed();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        WebGlInterop.DrawWithoutMesh((int)topology, firstVertex, vertexCount);
        _vertexArrayBinds++;
        RecordDraw(vertexCount, topology);
    }

    public void BeginFrame(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        WebGlInterop.BeginFrame(width, height);
        if (_frameWidth != width || _frameHeight != height)
        {
            ResetState();
            _frameWidth = width; _frameHeight = height;
        }
    }

    public void Present()
    {
        ThrowIfDisposed();
        WebGlInterop.Present();
    }

    public void SetVSync(bool enabled)
    {
        ThrowIfDisposed();
        // requestAnimationFrame is the browser presentation clock.
    }

    public GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region, int surfaceWidth, int surfaceHeight)
    {
        ThrowIfDisposed();
        try
        {
            region.Validate();
            if (region.X < 0 || region.Y < 0 || region.X + region.Width > surfaceWidth ||
                region.Y + region.Height > surfaceHeight)
                throw new ArgumentOutOfRangeException(nameof(region));
            var pixels = Convert.FromBase64String(WebGlInterop.ReadPixels(
                region.X, surfaceHeight - region.Y - region.Height, region.Width, region.Height));
            FlipRows(pixels, region.Width, region.Height);
            return GraphicsColorReadbackRequest.Ready(
                region, new GraphicsColorReadbackImage(region.Width, region.Height, pixels));
        }
        catch (Exception exception)
        {
            return GraphicsColorReadbackRequest.Failed(region, exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        ReleasePreparedImages();
        WebGlInterop.DisposeDevice();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RecordDraw(int vertexCount, GraphicsPrimitiveTopology topology) =>
        _drawStatistics = new GraphicsDrawStatistics(
            _drawStatistics.DrawCallCount + 1,
            _drawStatistics.VertexCount + vertexCount,
            _drawStatistics.TriangleCount +
            (topology == GraphicsPrimitiveTopology.TriangleList ? vertexCount / 3 : 0),
            _drawStatistics.LineCount +
            (topology == GraphicsPrimitiveTopology.LineList ? vertexCount / 2 : 0));

    private T Require<T>(IGraphicsResource resource) where T : WebGlResource
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is not T typed || !ReferenceEquals(typed.Device, this))
            throw new ArgumentException("The graphics resource belongs to another WebGL device.", nameof(resource));
        typed.ThrowIfDisposed();
        return typed;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string NormalizeShader(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var normalized = source.Replace("#version 330 core", "#version 300 es\nprecision highp float;",
            StringComparison.Ordinal);
        return normalized.Contains("#version 300 es", StringComparison.Ordinal)
            ? normalized
            : "#version 300 es\nprecision highp float;\n" + normalized;
    }

    // The browser copies pixel bytes during the synchronous import and never
    // writes into this read-only alias or retains its transient managed view.
    private static Span<byte> InteropPixelBytes(ReadOnlySpan<byte> bytes) =>
        MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length);

    // The supported browser ABI accepts Span<byte>, not ReadOnlySpan<float>.
    // JS only calls MemoryView.copyTo during this synchronous import; it never
    // writes into this alias or retains the transient managed view.
    private static Span<byte> InteropVertexBytes(ReadOnlySpan<float> values)
    {
        var bytes = MemoryMarshal.AsBytes(values);
        return MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length);
    }

    private static bool SameBits<T>(T left, T right) where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref left, 1)).SequenceEqual(
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref right, 1)));

    private static void FlipRows(byte[] pixels, int width, int height)
    {
        var rowLength = checked(width * 4);
        var temporary = new byte[rowLength];
        for (var top = 0; top < height / 2; top++)
        {
            var bottom = height - top - 1;
            var topRow = pixels.AsSpan(top * rowLength, rowLength);
            var bottomRow = pixels.AsSpan(bottom * rowLength, rowLength);
            topRow.CopyTo(temporary);
            bottomRow.CopyTo(topRow);
            temporary.CopyTo(bottomRow);
        }
    }

    private abstract class WebGlResource(WebGlGraphicsDevice device, string label) : IGraphicsResource
    {
        private bool _disposed;
        public IGraphicsDevice Device => device;
        protected WebGlGraphicsDevice Owner => device;
        public string Label { get; } = label;
        protected abstract void Release();
        public void Dispose()
        {
            if (_disposed) return;
            Release();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
        public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class WebGlProgram : WebGlResource, IGraphicsProgram
    {
        private const int MaximumCachedUniforms = 64;
        private const int MaximumCachedVectorArrayNames = 4096;
        private int _handle;
        private readonly Dictionary<string, UniformValue> _uniforms = new(StringComparer.Ordinal);
        private string? _vectorArrayFirstElementName;
        private string[] _vectorArrayElementNames = [];
        private enum UniformKind { Matrix, Vector, Float, Int }
        private readonly record struct UniformValue(long Generation, UniformKind Kind,
            int Scalar, NumericsVector4 Vector, NumericsMatrix4x4 Matrix);
        public WebGlProgram(WebGlGraphicsDevice device, string label, string vertex, string fragment)
            : base(device, label) => _handle = WebGlInterop.CreateProgram(label, vertex, fragment);
        public void Bind() { ThrowIfDisposed(); Owner.UseProgram(_handle); }
        public void SetMatrix4x4(string name, NumericsMatrix4x4 value)
        {
            ThrowIfDisposed();
            Bind();
            if (TryGetUniform(name, UniformKind.Matrix, out var cached) && SameBits(cached.Matrix, value))
            { Owner._cacheHits++; return; }
            WebGlInterop.SetUniformMatrix(_handle, name,
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44);
            CacheUniform(name, new(Owner._stateGeneration, UniformKind.Matrix, 0, default, value));
        }
        public void SetVector4(string name, NumericsVector4 value)
        {
            ThrowIfDisposed();
            Bind();
            if (TryGetUniform(name, UniformKind.Vector, out var cached) && SameBits(cached.Vector, value))
            { Owner._cacheHits++; return; }
            WebGlInterop.SetUniformVector(_handle, name, value.X, value.Y, value.Z, value.W);
            CacheUniform(name, new(Owner._stateGeneration, UniformKind.Vector, 0, value, default));
        }
        public void SetVector4Array(string firstElementName, ReadOnlySpan<NumericsVector4> values)
        {
            if (!firstElementName.EndsWith("[0]", StringComparison.Ordinal))
                throw new ArgumentException("Expected an array [0] uniform.", nameof(firstElementName));
            // The default interface implementation does not bind or check
            // disposal for an empty array. Keep that behavior here as well.
            if (values.IsEmpty) return;
            if (values.Length > MaximumCachedVectorArrayNames)
            {
                var prefix = firstElementName[..^3];
                for (var i = 0; i < values.Length; i++) SetVector4(prefix + "[" + i + "]", values[i]);
                return;
            }
            // Retain one array name per program. Arbitrary user uniform names
            // and oversized arrays cannot grow this cache without a bound.
            if (!string.Equals(_vectorArrayFirstElementName, firstElementName, StringComparison.Ordinal))
            {
                _vectorArrayFirstElementName = firstElementName;
                _vectorArrayElementNames = [];
            }
            if (_vectorArrayElementNames.Length < values.Length)
            {
                var prefix = firstElementName[..^3];
                var names = new string[values.Length];
                _vectorArrayElementNames.CopyTo(names, 0);
                for (var i = _vectorArrayElementNames.Length; i < names.Length; i++)
                    names[i] = prefix + "[" + i + "]";
                _vectorArrayElementNames = names;
            }
            // Preserve existing per-element cache, state generation and JS
            // behavior; only the repeated element-name construction is shared.
            for (var i = 0; i < values.Length; i++) SetVector4(_vectorArrayElementNames[i], values[i]);
        }
        public void SetFloat(string name, float value)
        {
            ThrowIfDisposed(); Bind();
            var bits = BitConverter.SingleToInt32Bits(value);
            if (TryGetUniform(name, UniformKind.Float, out var cached) && cached.Scalar == bits)
            { Owner._cacheHits++; return; }
            WebGlInterop.SetUniformFloat(_handle, name, value);
            CacheUniform(name, new(Owner._stateGeneration, UniformKind.Float, bits, default, default));
        }
        public void SetInt(string name, int value)
        {
            ThrowIfDisposed(); Bind();
            if (TryGetUniform(name, UniformKind.Int, out var cached) && cached.Scalar == value)
            { Owner._cacheHits++; return; }
            WebGlInterop.SetUniformInt(_handle, name, value);
            CacheUniform(name, new(Owner._stateGeneration, UniformKind.Int, value, default, default));
        }
        private bool TryGetUniform(string name, UniformKind kind, out UniformValue value) =>
            _uniforms.TryGetValue(name, out value) && value.Generation == Owner._stateGeneration && value.Kind == kind;
        private void CacheUniform(string name, UniformValue value)
        {
            // User shaders may generate names dynamically. Bound retained CPU
            // state without changing any GL uniform or evicting another program.
            if (_uniforms.Count >= MaximumCachedUniforms && !_uniforms.ContainsKey(name)) _uniforms.Clear();
            _uniforms[name] = value; Owner._uniformWrites++;
        }
        protected override void Release()
        {
            if (_handle != 0) WebGlInterop.DeleteProgram(_handle);
            if (Owner._boundProgram == _handle) Owner._boundProgram = -1;
            _handle = 0; _uniforms.Clear();
            _vectorArrayFirstElementName = null; _vectorArrayElementNames = [];
        }
    }

    private sealed class WebGlMesh : WebGlResource, IGraphicsMesh
    {
        private int _handle;
        public GraphicsVertexLayout Layout { get; }
        public GraphicsPrimitiveTopology Topology { get; }
        public GraphicsBufferUsage Usage { get; }
        public int VertexCount { get; private set; }
        public int Handle => _handle;

        public WebGlMesh(WebGlGraphicsDevice device, GraphicsMeshDescription description)
            : base(device, description.Label)
        {
            Layout = description.Layout;
            Topology = description.Topology;
            Usage = description.Usage;
            _handle = WebGlInterop.CreateMesh(
                JsonSerializer.Serialize(new WebGlLayoutInformation(Layout.StrideBytes,
                        Layout.Attributes.Select(item => new WebGlAttributeInformation(
                            item.Location, item.ComponentCount, item.OffsetBytes)).ToArray()),
                    WebGlJsonContext.Default.WebGlLayoutInformation),
                InteropVertexBytes(description.Vertices.Span), Usage == GraphicsBufferUsage.Dynamic);
            VertexCount = description.VertexCount;
        }

        public void Update(ReadOnlySpan<float> vertices)
        {
            ThrowIfDisposed();
            if (Usage != GraphicsBufferUsage.Dynamic)
                throw new InvalidOperationException($"Mesh '{Label}' is static.");
            var byteLength = checked(vertices.Length * sizeof(float));
            if (byteLength % Layout.StrideBytes != 0)
                throw new ArgumentException("Vertex data length must be a multiple of the layout stride.",
                    nameof(vertices));
            WebGlInterop.UpdateMesh(_handle, InteropVertexBytes(vertices));
            VertexCount = byteLength / Layout.StrideBytes;
        }

        protected override void Release()
        { if (_handle != 0) WebGlInterop.DeleteMesh(_handle); _handle = 0; VertexCount = 0; }
    }

    private sealed class WebGlTexture : WebGlResource, IGraphicsTexture2D
    {
        private int _handle;
        private readonly bool _ownsHandle;
        public GraphicsTextureDescription Description { get; }
        public int Handle => _handle;

        public WebGlTexture(WebGlGraphicsDevice device, string label,
            GraphicsTextureDescription description, ReadOnlySpan<byte> initialData, bool ownsHandle)
            : base(device, label)
        {
            Description = description;
            _ownsHandle = ownsHandle;
            if (!initialData.IsEmpty && initialData.Length != description.DataByteSize)
                throw new ArgumentException($"Texture '{label}' requires {description.DataByteSize} bytes.",
                    nameof(initialData));
            _handle = WebGlInterop.CreateTexture(label,
                JsonSerializer.Serialize(description, WebGlJsonContext.Default.GraphicsTextureDescription),
                InteropPixelBytes(initialData));
        }

        internal WebGlTexture(WebGlGraphicsDevice device, string label,
            GraphicsTextureDescription description, int handle, bool ownsHandle = false)
            : base(device, label)
        {
            Description = description;
            _handle = handle;
            _ownsHandle = ownsHandle;
        }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed();
            if (pixels.Length != Description.DataByteSize)
                throw new ArgumentException($"Texture '{Label}' requires {Description.DataByteSize} bytes.",
                    nameof(pixels));
            try
            {
                WebGlInterop.UpdateTexture(_handle,
                    JsonSerializer.Serialize(Description, WebGlJsonContext.Default.GraphicsTextureDescription),
                    InteropPixelBytes(pixels));
            }
            finally { Owner.InvalidateTextureBindings(); }
        }

        protected override void Release()
        {
            if (_ownsHandle && _handle != 0)
            {
                WebGlInterop.DeleteTexture(_handle);
                Owner.InvalidateTextureBindings();
            }
            _handle = 0;
        }
    }

    private sealed class WebGlRenderTarget : WebGlResource, IGraphicsRenderTarget
    {
        private int _handle;
        public int Handle => _handle;
        public int Width { get; }
        public int Height { get; }
        public IGraphicsTexture2D? ColorTexture { get; }
        public IGraphicsTexture2D? DepthTexture { get; }

        public WebGlRenderTarget(WebGlGraphicsDevice device, string label,
            GraphicsRenderTargetDescription description) : base(device, label)
        {
            Width = description.Width;
            Height = description.Height;
            var handles = JsonSerializer.Deserialize(
                WebGlInterop.CreateRenderTarget(label,
                    JsonSerializer.Serialize(description,
                        WebGlJsonContext.Default.GraphicsRenderTargetDescription)),
                WebGlJsonContext.Default.WebGlRenderTargetHandles);
            if (handles is null || handles.Framebuffer == 0)
                throw new InvalidOperationException($"WebGL render target '{label}' was not created.");
            _handle = handles.Framebuffer;
            if (description.ColorFormat is { } color && handles.Color != 0)
                ColorTexture = new WebGlTexture(device, label + ".Color",
                    new GraphicsTextureDescription(Width, Height, color,
                        GraphicsTextureUsage.RenderTarget |
                        (description.SampleColor ? GraphicsTextureUsage.Sampled : GraphicsTextureUsage.None),
                        description.Filter, description.Filter), handles.Color);
            if (description.DepthFormat is { } depth && handles.Depth != 0)
                DepthTexture = new WebGlTexture(device, label + ".Depth",
                    new GraphicsTextureDescription(Width, Height, depth,
                        GraphicsTextureUsage.RenderTarget |
                        (description.SampleDepth ? GraphicsTextureUsage.Sampled : GraphicsTextureUsage.None),
                        description.Filter, description.Filter), handles.Depth);
        }

        protected override void Release()
        {
            ColorTexture?.Dispose();
            DepthTexture?.Dispose();
            if (_handle != 0) WebGlInterop.DeleteRenderTarget(_handle);
            Owner.InvalidateTextureBindings();
            _handle = 0;
        }
    }

    private sealed class FramebufferScope(WebGlGraphicsDevice device, int previous) : IDisposable
    {
        private WebGlGraphicsDevice? _device = device;
        public void Dispose()
        {
            if (_device is null) return;
            _device.ThrowIfDisposed();
            WebGlInterop.RestoreRenderTarget(previous);
            _device = null;
        }
    }

}

internal sealed record WebGlDeviceInformation(
    string Vendor, string Renderer, string Version, int MaxTextureSize);
internal sealed record WebGlLayoutInformation(
    int StrideBytes, WebGlAttributeInformation[] Attributes);
internal sealed record WebGlAttributeInformation(int Location, int ComponentCount, int OffsetBytes);
internal sealed record WebGlRenderTargetHandles(int Framebuffer, int Color, int Depth);

[JsonSerializable(typeof(WebGlDeviceInformation))]
[JsonSerializable(typeof(WebGlLayoutInformation))]
[JsonSerializable(typeof(WebGlRenderTargetHandles))]
[JsonSerializable(typeof(GraphicsTextureDescription))]
[JsonSerializable(typeof(GraphicsRenderTargetDescription))]
internal sealed partial class WebGlJsonContext : JsonSerializerContext;

internal static partial class WebGlInterop
{
    [JSImport("globalThis.BEngineWebInterop.initialize")]
    internal static partial string Initialize(string canvasSelector);
    [JSImport("globalThis.BEngineWebInterop.beginFrame")]
    internal static partial void BeginFrame(int width, int height);
    [JSImport("globalThis.BEngineWebInterop.present")]
    internal static partial void Present();
    [JSImport("globalThis.BEngineWebInterop.disposeDevice")]
    internal static partial void DisposeDevice();
    [JSImport("globalThis.BEngineWebInterop.createProgram")]
    internal static partial int CreateProgram(string label, string vertex, string fragment);
    [JSImport("globalThis.BEngineWebInterop.bindProgram")]
    internal static partial void BindProgram(int handle);
    [JSImport("globalThis.BEngineWebInterop.deleteProgram")]
    internal static partial void DeleteProgram(int handle);
    [JSImport("globalThis.BEngineWebInterop.setUniformMatrix")]
    internal static partial void SetUniformMatrix(int handle, string name,
        float m11, float m12, float m13, float m14, float m21, float m22, float m23, float m24,
        float m31, float m32, float m33, float m34, float m41, float m42, float m43, float m44);
    [JSImport("globalThis.BEngineWebInterop.setUniformVector")]
    internal static partial void SetUniformVector(int handle, string name, float x, float y, float z, float w);
    [JSImport("globalThis.BEngineWebInterop.setUniformFloat")]
    internal static partial void SetUniformFloat(int handle, string name, float value);
    [JSImport("globalThis.BEngineWebInterop.setUniformInt")]
    internal static partial void SetUniformInt(int handle, string name, int value);
    [JSImport("globalThis.BEngineWebInterop.createMesh")]
    internal static partial int CreateMesh(string layoutJson,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices, bool dynamic);
    [JSImport("globalThis.BEngineWebInterop.updateMesh")]
    internal static partial void UpdateMesh(int handle, [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices);
    [JSImport("globalThis.BEngineWebInterop.deleteMesh")]
    internal static partial void DeleteMesh(int handle);
    [JSImport("globalThis.BEngineWebInterop.createTexture")]
    internal static partial int CreateTexture(string label, string descriptionJson,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
    [JSImport("globalThis.BEngineWebInterop.updateTexture")]
    internal static partial void UpdateTexture(int handle, string descriptionJson,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
    [JSImport("globalThis.BEngineWebInterop.deleteTexture")]
    internal static partial void DeleteTexture(int handle);
    [JSImport("globalThis.BEngineWebInterop.createRenderTarget")]
    internal static partial string CreateRenderTarget(string label, string descriptionJson);
    [JSImport("globalThis.BEngineWebInterop.deleteRenderTarget")]
    internal static partial void DeleteRenderTarget(int handle);
    [JSImport("globalThis.BEngineWebInterop.bindRenderTarget")]
    internal static partial int BindRenderTarget(int handle);
    [JSImport("globalThis.BEngineWebInterop.restoreRenderTarget")]
    internal static partial void RestoreRenderTarget(int handle);
    [JSImport("globalThis.BEngineWebInterop.setViewport")]
    internal static partial void SetViewport(int x, int y, int width, int height);
    [JSImport("globalThis.BEngineWebInterop.disableScissor")]
    internal static partial void DisableScissor();
    [JSImport("globalThis.BEngineWebInterop.setScissor")]
    internal static partial void SetScissor(int x, int y, int width, int height);
    [JSImport("globalThis.BEngineWebInterop.clear")]
    internal static partial void Clear(int flags, float r, float g, float b, float a);
    [JSImport("globalThis.BEngineWebInterop.setDepthState")]
    internal static partial void SetDepthState(bool test, bool write);
    [JSImport("globalThis.BEngineWebInterop.setBlendMode")]
    internal static partial void SetBlendMode(int mode);
    [JSImport("globalThis.BEngineWebInterop.setRasterizerState")]
    internal static partial void SetRasterizerState(bool depthBias, float slope, float constantBias, int cullMode);
    [JSImport("globalThis.BEngineWebInterop.bindTexture")]
    internal static partial void BindTexture(int slot, int handle);
    [JSImport("globalThis.BEngineWebInterop.drawMesh")]
    internal static partial void DrawMesh(int handle, int topology, int firstVertex, int vertexCount);
    [JSImport("globalThis.BEngineWebInterop.drawWithoutMesh")]
    internal static partial void DrawWithoutMesh(int topology, int firstVertex, int vertexCount);
    [JSImport("globalThis.BEngineWebInterop.readPixels")]
    internal static partial string ReadPixels(int x, int y, int width, int height);
}
