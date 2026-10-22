using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine.Rendering.Rhi;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Platform;

/// <summary>WebGL 2 implementation used by the browser Player Host.</summary>
public sealed class WebGlGraphicsDevice : IGraphicsPresentationDevice, IGraphicsDeviceStatistics,
    IGraphicsTextureFormatSupport, IGraphicsColorReadback
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

    public WebGlGraphicsDevice(string canvasSelector = "#bengine-game")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasSelector);
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
        return new WebGlTexture(this, label, description, initialData, ownsHandle: true);
    }

    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        return new WebGlRenderTarget(this, label, description);
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
        _viewport = viewport;
        WebGlInterop.SetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        ThrowIfDisposed();
        if (scissor is null)
        {
            WebGlInterop.DisableScissor();
            return;
        }
        scissor.Value.Validate();
        WebGlInterop.SetScissor(
            _viewport.X + scissor.Value.X,
            _viewport.Y + _viewport.Height - scissor.Value.Y - scissor.Value.Height,
            scissor.Value.Width,
            scissor.Value.Height);
    }

    public void Clear(GraphicsClearFlags flags, NumericsVector4 color)
    {
        ThrowIfDisposed();
        WebGlInterop.Clear((int)flags, color.X, color.Y, color.Z, color.W);
    }

    public void SetDepthState(GraphicsDepthState state)
    {
        ThrowIfDisposed();
        WebGlInterop.SetDepthState(state.TestEnabled, state.WriteEnabled);
    }

    public void SetBlendMode(GraphicsBlendMode mode)
    {
        ThrowIfDisposed();
        WebGlInterop.SetBlendMode((int)mode);
    }

    public void SetRasterizerState(GraphicsRasterizerState state)
    {
        ThrowIfDisposed();
        WebGlInterop.SetRasterizerState(
            state.DepthBiasEnabled, state.SlopeScale, state.ConstantBias, (int)state.CullMode);
    }

    public void BindTexture(int slot, IGraphicsTexture2D texture)
    {
        ThrowIfDisposed();
        if (slot is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(slot));
        var typed = Require<WebGlTexture>(texture);
        if ((typed.Description.Usage & GraphicsTextureUsage.Sampled) == 0)
            throw new InvalidOperationException($"Texture '{texture.Label}' was not created for sampling.");
        WebGlInterop.BindTexture(slot, typed.Handle);
    }

    public void Draw(IGraphicsMesh mesh)
    {
        var typed = Require<WebGlMesh>(mesh);
        Draw(typed, typed.VertexCount, 0);
    }

    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
    {
        var typed = Require<WebGlMesh>(mesh);
        if (vertexCount < 0 || firstVertex < 0 || firstVertex + vertexCount > typed.VertexCount)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        WebGlInterop.DrawMesh(typed.Handle, (int)typed.Topology, firstVertex, vertexCount);
        RecordDraw(vertexCount, typed.Topology);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        ThrowIfDisposed();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        WebGlInterop.DrawWithoutMesh((int)topology, firstVertex, vertexCount);
        RecordDraw(vertexCount, topology);
    }

    public void BeginFrame(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        WebGlInterop.BeginFrame(width, height);
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

    private static string Encode(ReadOnlySpan<byte> bytes) =>
        bytes.IsEmpty ? string.Empty : Convert.ToBase64String(bytes);

    private static string Encode(ReadOnlySpan<float> values) =>
        Encode(MemoryMarshal.AsBytes(values));

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
        private int _handle;
        public WebGlProgram(WebGlGraphicsDevice device, string label, string vertex, string fragment)
            : base(device, label) => _handle = WebGlInterop.CreateProgram(label, vertex, fragment);
        public void Bind() { ThrowIfDisposed(); WebGlInterop.BindProgram(_handle); }
        public void SetMatrix4x4(string name, NumericsMatrix4x4 value)
        {
            ThrowIfDisposed();
            WebGlInterop.SetUniformMatrix(_handle, name,
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44);
        }
        public void SetVector4(string name, NumericsVector4 value)
        {
            ThrowIfDisposed();
            WebGlInterop.SetUniformVector(_handle, name, value.X, value.Y, value.Z, value.W);
        }
        public void SetFloat(string name, float value)
        { ThrowIfDisposed(); WebGlInterop.SetUniformFloat(_handle, name, value); }
        public void SetInt(string name, int value)
        { ThrowIfDisposed(); WebGlInterop.SetUniformInt(_handle, name, value); }
        protected override void Release()
        { if (_handle != 0) WebGlInterop.DeleteProgram(_handle); _handle = 0; }
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
                Encode(description.Vertices.Span), Usage == GraphicsBufferUsage.Dynamic);
            VertexCount = description.VertexCount;
        }

        public void Update(ReadOnlySpan<float> vertices)
        {
            ThrowIfDisposed();
            if (Usage != GraphicsBufferUsage.Dynamic)
                throw new InvalidOperationException($"Mesh '{Label}' is static.");
            if (vertices.Length * sizeof(float) % Layout.StrideBytes != 0)
                throw new ArgumentException("Vertex data length must be a multiple of the layout stride.",
                    nameof(vertices));
            WebGlInterop.UpdateMesh(_handle, Encode(vertices));
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
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
                Encode(initialData));
        }

        internal WebGlTexture(WebGlGraphicsDevice device, string label,
            GraphicsTextureDescription description, int handle)
            : base(device, label)
        {
            Description = description;
            _handle = handle;
            _ownsHandle = false;
        }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed();
            if (pixels.Length != Description.DataByteSize)
                throw new ArgumentException($"Texture '{Label}' requires {Description.DataByteSize} bytes.",
                    nameof(pixels));
            WebGlInterop.UpdateTexture(_handle,
                JsonSerializer.Serialize(Description, WebGlJsonContext.Default.GraphicsTextureDescription),
                Encode(pixels));
        }

        protected override void Release()
        { if (_ownsHandle && _handle != 0) WebGlInterop.DeleteTexture(_handle); _handle = 0; }
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
    internal static partial int CreateMesh(string layoutJson, string verticesBase64, bool dynamic);
    [JSImport("globalThis.BEngineWebInterop.updateMesh")]
    internal static partial void UpdateMesh(int handle, string verticesBase64);
    [JSImport("globalThis.BEngineWebInterop.deleteMesh")]
    internal static partial void DeleteMesh(int handle);
    [JSImport("globalThis.BEngineWebInterop.createTexture")]
    internal static partial int CreateTexture(string label, string descriptionJson, string dataBase64);
    [JSImport("globalThis.BEngineWebInterop.updateTexture")]
    internal static partial void UpdateTexture(int handle, string descriptionJson, string dataBase64);
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
