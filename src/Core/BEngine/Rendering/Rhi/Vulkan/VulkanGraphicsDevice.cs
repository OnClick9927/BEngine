using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;
using Vd = Veldrid;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi.Vulkan;

public sealed class VulkanGraphicsDevice : IGraphicsPresentationDevice, IGraphicsResourceRetirement,
    IGraphicsDeviceStatistics, IGraphicsColorReadback
{
    private const GraphicsDeviceFeatures SupportedFeatures =
        GraphicsDeviceFeatures.Rasterization |
        GraphicsDeviceFeatures.ShaderPrograms |
        GraphicsDeviceFeatures.StaticVertexBuffers |
        GraphicsDeviceFeatures.DynamicVertexBuffers |
        GraphicsDeviceFeatures.SampledTextures |
        GraphicsDeviceFeatures.AlphaBlending |
        GraphicsDeviceFeatures.ScissorRectangles |
        GraphicsDeviceFeatures.DepthBias;

    private readonly Vd.GraphicsDevice _device;
    private readonly ResourceFactory _factory;
    private readonly CommandList _commands;
    private readonly Fence _frameFence;
    private readonly List<IDisposable> _retiredResources = [];
    private readonly List<VulkanColorReadbackRequest> _recordingReadbacks = [];
    private readonly List<VulkanColorReadbackRequest> _submittedReadbacks = [];
    private readonly GraphicsDeviceCapabilities _capabilities;
    private VulkanProgram? _activeProgram;
    private VulkanTexture2D? _boundTexture;
    private GraphicsDepthState _depthState = GraphicsDepthState.Default;
    private GraphicsBlendMode _blendMode;
    private GraphicsRasterizerState _rasterizerState = GraphicsRasterizerState.Default;
    private GraphicsRect _viewport;
    private int _frameWidth;
    private int _frameHeight;
    private GraphicsDrawStatistics _drawStatistics;
    private bool _frameOpen;
    private bool _frameSubmitted;
    private bool _disposed;

    public GraphicsBackend Backend
    {
        get { return GraphicsBackend.Vulkan; }
    }
    public GraphicsDeviceCapabilities Capabilities
    {
        get { return _capabilities; }
    }
    public GraphicsDrawStatistics DrawStatistics => _drawStatistics;
    public GraphicsColorReadbackCapabilities ColorReadbackCapabilities => new(
        true,
        GraphicsColorReadbackValidation.DefaultMaximumBytes);

    internal Vd.GraphicsDevice NativeDevice => _device;
    internal ResourceFactory Factory => _factory;
    internal CommandList Commands => _commands;
    internal GraphicsDepthState DepthState => _depthState;
    internal GraphicsBlendMode BlendMode => _blendMode;
    internal GraphicsRasterizerState RasterizerState => _rasterizerState;

    internal VulkanGraphicsDevice(Vd.GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _factory = device.ResourceFactory;
        _commands = _factory.CreateCommandList();
        _frameFence = _factory.CreateFence(false);
        _capabilities = new GraphicsDeviceCapabilities(
            GraphicsBackend.Vulkan,
            device.DeviceName,
            $"Vulkan {device.ApiVersion}",
            SupportedFeatures,
            [GraphicsShaderLanguage.Glsl, GraphicsShaderLanguage.SpirV]);
        GraphicsBackendSettings.ReportActive(_capabilities);
    }

    public void BeginFrame(int width, int height)
    {
        ThrowIfDisposed();
        if (_frameOpen) throw new InvalidOperationException("The Vulkan frame is already open.");
        CompleteSubmittedFrame();
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width != _frameWidth || height != _frameHeight)
        {
            _device.MainSwapchain.Resize((uint)width, (uint)height);
            _frameWidth = width;
            _frameHeight = height;
        }
        _commands.Begin();
        _commands.SetFramebuffer(_device.MainSwapchain.Framebuffer);
        _frameOpen = true;
        SetViewportCore(new GraphicsRect(0, 0, width, height));
    }

    public void Present()
    {
        ThrowIfDisposed();
        if (!_frameOpen) throw new InvalidOperationException("No Vulkan frame is open.");
        _commands.End();
        try
        {
            _device.SubmitCommands(_commands, _frameFence);
            _frameSubmitted = true;
            SubmitReadbacks();
            _device.SwapBuffers();
        }
        catch (Exception exception)
        {
            if (!_frameSubmitted) AbandonRecordingReadbacks(exception.Message);
            throw;
        }
        finally
        {
            _frameOpen = false;
        }
    }

    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        description.Validate();
        if (description.VertexShader.Language is not GraphicsShaderLanguage.Glsl and not GraphicsShaderLanguage.SpirV)
            throw new NotSupportedException("The Vulkan provider accepts Vulkan GLSL or SPIR-V shader source.");
        return new VulkanProgram(this, description);
    }

    public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        return new VulkanMesh(this, description);
    }

    public IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        return new VulkanTexture2D(this, label, description, initialData);
    }

    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description)
    {
        throw new NotSupportedException("Vulkan offscreen render targets are not enabled in this lightweight provider yet.");
    }

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
    {
        throw new NotSupportedException("Vulkan offscreen render targets are not enabled in this lightweight provider yet.");
    }

    public GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region,
        int surfaceWidth,
        int surfaceHeight)
    {
        ThrowIfDisposed();
        RequireFrame();
        GraphicsColorReadbackValidation.ValidateRegion(
            region,
            surfaceWidth,
            surfaceHeight,
            GraphicsColorReadbackValidation.DefaultMaximumBytes);
        if (surfaceWidth != _frameWidth || surfaceHeight != _frameHeight)
            return GraphicsColorReadbackRequest.Unavailable(region,
                "The requested surface size does not match the active Vulkan swapchain.");

        var colorTargets = _device.MainSwapchain.Framebuffer.ColorTargets;
        if (colorTargets.Count == 0)
            return GraphicsColorReadbackRequest.Unavailable(region,
                "The active Vulkan framebuffer has no color attachment.");
        var source = colorTargets[0].Target;
        if (!VulkanColorReadbackRequest.Supports(source.Format))
            return GraphicsColorReadbackRequest.Unavailable(region,
                $"Vulkan color readback does not support {source.Format}.");

        Vd.Texture? staging = null;
        try
        {
            var framebufferRegion = ToFramebufferReadRegion(region);
            staging = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)region.Width,
                (uint)region.Height,
                1,
                1,
                source.Format,
                TextureUsage.Staging));
            _commands.CopyTexture(
                source,
                (uint)framebufferRegion.X,
                (uint)framebufferRegion.Y,
                0,
                0,
                0,
                staging,
                0,
                0,
                0,
                0,
                0,
                (uint)region.Width,
                (uint)region.Height,
                1,
                1);
            var request = new VulkanColorReadbackRequest(this, region, staging, source.Format);
            staging = null;
            _recordingReadbacks.Add(request);
            return request;
        }
        catch (Exception exception)
        {
            staging?.Dispose();
            return GraphicsColorReadbackRequest.Failed(region, exception.Message);
        }
    }

    internal static GraphicsRect ToFramebufferReadRegion(GraphicsRect region) => region;

    public void SetViewport(GraphicsRect viewport)
    {
        SetViewportCore(viewport);
    }

    private void SetViewportCore(GraphicsRect viewport)
    {
        RequireFrame();
        viewport.Validate();
        _viewport = viewport;
        _commands.SetViewport(0, new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, 0, 1));
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        RequireFrame();
        var value = scissor ?? new GraphicsRect(0, 0, _viewport.Width, _viewport.Height);
        value.Validate();
        _commands.SetScissorRect(0,
            (uint)Math.Max(0, _viewport.X + value.X),
            (uint)Math.Max(0, _viewport.Y + value.Y),
            (uint)Math.Max(0, value.Width), (uint)Math.Max(0, value.Height));
    }

    public void Clear(GraphicsClearFlags flags, NVector4 color)
    {
        RequireFrame();
        if ((flags & GraphicsClearFlags.Color) != 0)
            _commands.ClearColorTarget(0, new RgbaFloat(color.X, color.Y, color.Z, color.W));
        if ((flags & (GraphicsClearFlags.Depth | GraphicsClearFlags.Stencil)) != 0)
            _commands.ClearDepthStencil((flags & GraphicsClearFlags.Depth) != 0 ? 1f : 0f,
                (flags & GraphicsClearFlags.Stencil) != 0 ? (byte)0 : (byte)0);
    }

    public void SetDepthState(GraphicsDepthState state)
    {
        _depthState = state;
    }
    public void SetBlendMode(GraphicsBlendMode mode)
    {
        _blendMode = mode;
    }
    public void SetRasterizerState(GraphicsRasterizerState state)
    {
        _rasterizerState = state;
    }

    public void BindTexture(int slot, IGraphicsTexture2D texture)
    {
        if (slot != 0) throw new NotSupportedException("The lightweight Vulkan provider exposes texture slot 0.");
        _boundTexture = RequireResource<VulkanTexture2D>(texture);
        _boundTexture.MarkSampled();
    }

    public void Draw(IGraphicsMesh mesh)
    {
        var vkMesh = RequireResource<VulkanMesh>(mesh);
        Draw(vkMesh, vkMesh.VertexCountUnchecked, 0);
    }

    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
    {
        var vkMesh = RequireResource<VulkanMesh>(mesh);
        Draw(vkMesh, vertexCount, firstVertex);
    }

    private void Draw(VulkanMesh vkMesh, int vertexCount, int firstVertex)
    {
        RequireFrame();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        if (firstVertex + vertexCount > vkMesh.VertexCountUnchecked)
            throw new ArgumentOutOfRangeException(nameof(vertexCount), "The draw range exceeds the mesh vertex count.");
        var program = _activeProgram ?? throw new InvalidOperationException("No Vulkan shader program is bound.");
        program.Prepare(vkMesh, _boundTexture);
        _commands.SetVertexBuffer(0, vkMesh.Buffer);
        _commands.Draw((uint)vertexCount, 1, (uint)firstVertex, 0);
        _drawStatistics = _drawStatistics.AddDraw(vertexCount, vkMesh.TopologyUnchecked);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        throw new NotSupportedException("The Vulkan provider requires an explicit mesh vertex buffer.");
    }

    private void Bind(VulkanProgram program)
    {
        RequireFrame();
        _activeProgram = program;
    }

    internal void UpdateBuffer<T>(DeviceBuffer buffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        if (_frameOpen)
        {
            _commands.UpdateBuffer(buffer, 0, data);
            return;
        }
        CompleteSubmittedFrame();
        _device.UpdateBuffer(buffer, 0, data);
    }

    public void RetireResource(IDisposable resource)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(resource);
        if (ReferenceEquals(resource, _boundTexture)) _boundTexture = null;
        if (_frameOpen)
        {
            _retiredResources.Add(resource);
            return;
        }
        CompleteSubmittedFrame();
        resource.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_frameOpen)
        {
            try { _commands.End(); }
            catch { }
            _frameOpen = false;
            AbandonRecordingReadbacks("The Vulkan device was disposed before the readback frame was submitted.");
        }
        _device.WaitForIdle();
        AbandonSubmittedReadbacks("The Vulkan device was disposed before the readback result was consumed.");
        _frameSubmitted = false;
        _fallbackTexture?.Dispose();
        _fallbackTexture = null;
        _commands.Dispose();
        DisposeRetiredResources();
        _frameFence.Dispose();
        _device.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private T RequireResource<T>(IGraphicsResource resource) where T : VulkanResource
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is not T typed)
            throw new ArgumentException("The resource is not a Vulkan resource.", nameof(resource));
        if (!ReferenceEquals(typed.VulkanDevice, this))
            throw new ArgumentException("The graphics resource belongs to another device.", nameof(resource));
        return typed;
    }

    private void RequireFrame()
    {
        ThrowIfDisposed();
        if (!_frameOpen) throw new InvalidOperationException("BeginFrame must be called before recording Vulkan commands.");
    }

    private void CompleteSubmittedFrame()
    {
        if (_frameSubmitted)
        {
            _device.WaitForFence(_frameFence);
            _device.ResetFence(_frameFence);
            _frameSubmitted = false;
            CompleteSubmittedReadbacks();
        }
        DisposeRetiredResources();
    }

    private void SubmitReadbacks()
    {
        if (_recordingReadbacks.Count == 0) return;
        _submittedReadbacks.AddRange(_recordingReadbacks);
        _recordingReadbacks.Clear();
    }

    private void CompleteSubmittedReadbacks()
    {
        foreach (var request in _submittedReadbacks) request.MarkGpuCompleted();
        _submittedReadbacks.Clear();
    }

    private void AbandonRecordingReadbacks(string error)
    {
        foreach (var request in _recordingReadbacks) request.Abandon(error);
        _recordingReadbacks.Clear();
    }

    private void AbandonSubmittedReadbacks(string error)
    {
        foreach (var request in _submittedReadbacks) request.Abandon(error);
        _submittedReadbacks.Clear();
    }

    private bool TryCompleteReadback(VulkanColorReadbackRequest request)
    {
        if (request.GpuCompleted) return true;
        if (!_frameOpen && _frameSubmitted && _submittedReadbacks.Contains(request))
            CompleteSubmittedFrame();
        return request.GpuCompleted;
    }

    private void DisposeRetiredResources()
    {
        foreach (var resource in _retiredResources) resource.Dispose();
        _retiredResources.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class VulkanColorReadbackRequest : GraphicsColorReadbackRequest
    {
        private VulkanGraphicsDevice? _owner;
        private Vd.Texture? _staging;
        private readonly PixelFormat _format;
        private GraphicsColorReadbackImage? _image;
        private string _error = string.Empty;
        private bool _gpuCompleted;
        private bool _disposed;

        internal VulkanColorReadbackRequest(
            VulkanGraphicsDevice owner,
            GraphicsRect region,
            Vd.Texture staging,
            PixelFormat format)
        {
            _owner = owner;
            Region = region;
            _staging = staging;
            _format = format;
        }

        public override GraphicsRect Region { get; }
        internal bool GpuCompleted => _gpuCompleted;

        public override GraphicsColorReadbackStatus Status
        {
            get
            {
                if (_disposed) return GraphicsColorReadbackStatus.Disposed;
                if (_image is not null) return GraphicsColorReadbackStatus.Ready;
                if (!string.IsNullOrEmpty(_error)) return GraphicsColorReadbackStatus.Failed;
                return GraphicsColorReadbackStatus.Pending;
            }
        }

        public override string Error => _disposed
            ? "The color readback request was disposed."
            : _error;

        public override bool TryGetResult(out GraphicsColorReadbackImage? image)
        {
            image = null;
            if (_disposed || !string.IsNullOrEmpty(_error)) return false;
            if (_image is not null)
            {
                image = _image;
                return true;
            }
            var owner = _owner;
            if (owner is null || !owner.TryCompleteReadback(this)) return false;
            Resolve(owner);
            image = _image;
            return image is not null;
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_gpuCompleted || _owner is null) ReleaseStaging();
            _image = null;
        }

        internal void MarkGpuCompleted()
        {
            _gpuCompleted = true;
            if (_disposed) ReleaseStaging();
        }

        internal void Abandon(string error)
        {
            _gpuCompleted = true;
            if (!_disposed) _error = string.IsNullOrWhiteSpace(error) ? "Vulkan color readback failed." : error;
            ReleaseStaging();
            _owner = null;
        }

        internal static bool Supports(PixelFormat format) => format is
            PixelFormat.R8_G8_B8_A8_UNorm or
            PixelFormat.R8_G8_B8_A8_UNorm_SRgb or
            PixelFormat.B8_G8_R8_A8_UNorm or
            PixelFormat.B8_G8_R8_A8_UNorm_SRgb;

        private void Resolve(VulkanGraphicsDevice owner)
        {
            var staging = _staging;
            if (staging is null) return;
            try
            {
                var mapped = owner._device.Map(staging, MapMode.Read);
                try
                {
                    var rowBytes = checked(Region.Width * 4);
                    var pixels = new byte[checked(rowBytes * Region.Height)];
                    for (var row = 0; row < Region.Height; row++)
                    {
                        var sourceOffset = checked((int)(mapped.RowPitch * (uint)row));
                        Marshal.Copy(IntPtr.Add(mapped.Data, sourceOffset), pixels, row * rowBytes, rowBytes);
                    }
                    if (_format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb)
                    {
                        for (var offset = 0; offset < pixels.Length; offset += 4)
                            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
                    }
                    _image = GraphicsColorReadbackImage.FromOwnedRgba8(
                        Region.Width,
                        Region.Height,
                        pixels);
                }
                finally
                {
                    owner._device.Unmap(staging);
                }
            }
            catch (Exception exception)
            {
                _error = exception.Message;
            }
            finally
            {
                ReleaseStaging();
                _owner = null;
            }
        }

        private void ReleaseStaging()
        {
            var staging = Interlocked.Exchange(ref _staging, null);
            staging?.Dispose();
        }
    }

    private abstract class VulkanResource : IGraphicsResource
    {
        private readonly string _label;

        protected VulkanResource(VulkanGraphicsDevice device, string label)
        {
            VulkanDevice = device;
            _label = label;
        }

        public IGraphicsDevice Device
        {
            get { return VulkanDevice; }
        }
        internal VulkanGraphicsDevice VulkanDevice { get; }
        public string Label
        {
            get { return _label; }
        }
        public abstract void Dispose();
    }

    private sealed class VulkanMesh : VulkanResource, IGraphicsMesh
    {
        private DeviceBuffer _buffer;
        private int _capacityBytes;
        private bool _disposed;
        private readonly GraphicsVertexLayout _layout;
        private readonly GraphicsPrimitiveTopology _topology;
        private readonly GraphicsBufferUsage _usage;
        private int _vertexCount;

        public GraphicsVertexLayout Layout
        {
            get { return _layout; }
        }
        public GraphicsPrimitiveTopology Topology
        {
            get { return _topology; }
        }
        public GraphicsBufferUsage Usage
        {
            get { return _usage; }
        }
        public int VertexCount
        {
            get { return _vertexCount; }
        }
        internal GraphicsVertexLayout LayoutUnchecked => _layout;
        internal GraphicsPrimitiveTopology TopologyUnchecked => _topology;
        internal int VertexCountUnchecked => _vertexCount;
        public DeviceBuffer Buffer => _buffer;

        public VulkanMesh(VulkanGraphicsDevice device, GraphicsMeshDescription description)
            : base(device, description.Label)
        {
            _layout = description.Layout;
            _topology = description.Topology;
            _usage = description.Usage;
            _capacityBytes = Math.Max(16, description.Vertices.Length * sizeof(float));
            _buffer = device.Factory.CreateBuffer(new BufferDescription((uint)_capacityBytes,
                BufferUsage.VertexBuffer | (_usage == GraphicsBufferUsage.Dynamic ? BufferUsage.Dynamic : 0)));
            if (!description.Vertices.IsEmpty) Update(description.Vertices.Span);
        }

        public void Update(ReadOnlySpan<float> vertices)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var bytes = vertices.Length * sizeof(float);
            if (bytes > _capacityBytes)
            {
                var previous = _buffer;
                _capacityBytes = Math.Max(bytes, _capacityBytes * 2);
                _buffer = VulkanDevice.Factory.CreateBuffer(new BufferDescription((uint)_capacityBytes,
                    BufferUsage.VertexBuffer | BufferUsage.Dynamic));
                VulkanDevice.RetireResource(previous);
            }
            if (!vertices.IsEmpty) VulkanDevice.UpdateBuffer(_buffer, vertices);
            _vertexCount = bytes / _layout.StrideBytes;
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            VulkanDevice.RetireResource(_buffer);
        }
    }

    private sealed class VulkanTexture2D : VulkanResource, IGraphicsTexture2D
    {
        private bool _disposed;
        private bool _sampledSinceUpdate;
        private readonly GraphicsTextureDescription _description;
        internal event Action<VulkanTexture2D>? Disposing;
        public GraphicsTextureDescription Description
        {
            get { return _description; }
        }
        public Vd.Texture Texture { get; }
        public TextureView View { get; }
        public Sampler Sampler { get; }

        public VulkanTexture2D(
            VulkanGraphicsDevice device,
            string label,
            GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData) : base(device, label)
        {
            _description = description;
            Texture = device.Factory.CreateTexture(TextureDescription.Texture2D(
                (uint)description.Width, (uint)description.Height, 1, 1,
                ToVeldrid(description.Format), TextureUsage.Sampled));
            View = device.Factory.CreateTextureView(Texture);
            var addressMode = ToVeldrid(description.AddressMode);
            Sampler = device.Factory.CreateSampler(new SamplerDescription(
                addressMode,
                addressMode,
                addressMode,
                ToVeldrid(description.MinFilter, description.MagFilter),
                null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));
            if (!initialData.IsEmpty) Update(initialData);
        }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var required = _description.Width * _description.Height *
                           (_description.Format == GraphicsTextureFormat.R8Unorm ? 1 : 4);
            if (pixels.Length < required) throw new ArgumentException("Texture pixel data is incomplete.", nameof(pixels));
            // A mutable atlas can still be sampled by the previous submitted frame. Updating it in place
            // without synchronizing leaves newly-added glyphs undefined on Vulkan while old regions survive.
            if (_sampledSinceUpdate)
            {
                VulkanDevice.NativeDevice.WaitForIdle();
                _sampledSinceUpdate = false;
            }
            VulkanDevice.NativeDevice.UpdateTexture(Texture, pixels[..required], 0, 0, 0,
                (uint)_description.Width, (uint)_description.Height, 1, 0, 0);
        }

        public void MarkSampled() => _sampledSinceUpdate = true;

        public override void Dispose()
        {
            if (_disposed) return;
            Disposing?.Invoke(this);
            Disposing = null;
            Sampler.Dispose();
            View.Dispose();
            Texture.Dispose();
            _disposed = true;
        }

        private static SamplerAddressMode ToVeldrid(GraphicsTextureAddressMode addressMode) => addressMode switch
        {
            GraphicsTextureAddressMode.ClampToEdge => SamplerAddressMode.Clamp,
            GraphicsTextureAddressMode.Repeat => SamplerAddressMode.Wrap,
            GraphicsTextureAddressMode.MirroredRepeat => SamplerAddressMode.Mirror,
            _ => throw new ArgumentOutOfRangeException(nameof(addressMode))
        };

        private static SamplerFilter ToVeldrid(
            GraphicsTextureFilter minFilter,
            GraphicsTextureFilter magFilter) => (minFilter, magFilter) switch
        {
            (GraphicsTextureFilter.Nearest, GraphicsTextureFilter.Nearest) =>
                SamplerFilter.MinPoint_MagPoint_MipPoint,
            (GraphicsTextureFilter.Nearest, GraphicsTextureFilter.Linear) =>
                SamplerFilter.MinPoint_MagLinear_MipPoint,
            (GraphicsTextureFilter.Linear, GraphicsTextureFilter.Nearest) =>
                SamplerFilter.MinLinear_MagPoint_MipPoint,
            (GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear) =>
                SamplerFilter.MinLinear_MagLinear_MipPoint,
            _ => throw new ArgumentOutOfRangeException(nameof(minFilter))
        };

        private static PixelFormat ToVeldrid(GraphicsTextureFormat format) => format switch
        {
            GraphicsTextureFormat.R8Unorm => PixelFormat.R8_UNorm,
            GraphicsTextureFormat.Rgba8Unorm => PixelFormat.R8_G8_B8_A8_UNorm,
            _ => throw new NotSupportedException($"Texture format {format} is not supported for sampled Vulkan textures.")
        };
    }

    private sealed class VulkanProgram : VulkanResource, IGraphicsProgram
    {
        private readonly Vd.Shader[] _shaders;
        private readonly DeviceBuffer _uniformBuffer;
        private readonly ResourceLayout _uniformLayout;
        private readonly bool _textured;
        private readonly Dictionary<PipelineKey, Pipeline> _pipelines = [];
        private readonly Dictionary<VulkanTexture2D, ResourceSet> _resourceSets = [];
        private readonly float[] _uniforms = new float[4];
        private bool _uniformDirty = true;
        private bool _disposed;

        public VulkanProgram(VulkanGraphicsDevice device, GraphicsShaderProgramDescription description)
            : base(device, description.Label)
        {
            _textured = description.Label.Contains("Texture", StringComparison.OrdinalIgnoreCase);
            var vertex = new ShaderDescription(ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(description.VertexShader.Code), description.VertexShader.EntryPoint);
            var fragment = new ShaderDescription(ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(description.FragmentShader.Code), description.FragmentShader.EntryPoint);
            _shaders = device.Factory.CreateFromSpirv(vertex, fragment);
            _uniformBuffer = device.Factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _uniformLayout = device.Factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UIViewport", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UITexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("UISampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        }

        public void Bind()
        {
            VulkanDevice.Bind(this);
        }
        public void SetMatrix4x4(string name, NMatrix4x4 value) { }
        public void SetVector4(string name, NVector4 value) { }
        public void SetFloat(string name, float value)
        {
            if (name == "uViewportWidth") _uniforms[0] = value;
            else if (name == "uViewportHeight") _uniforms[1] = value;
            _uniformDirty = true;
        }
        public void SetInt(string name, int value) { }

        public void Prepare(VulkanMesh mesh, VulkanTexture2D? texture)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_uniformDirty)
            {
                VulkanDevice.UpdateBuffer<float>(_uniformBuffer, _uniforms);
                _uniformDirty = false;
            }
            var key = new PipelineKey(mesh.LayoutUnchecked, mesh.TopologyUnchecked, VulkanDevice.BlendMode,
                VulkanDevice.DepthState, VulkanDevice.RasterizerState);
            if (!_pipelines.TryGetValue(key, out var pipeline))
            {
                pipeline = CreatePipeline(mesh, key);
                _pipelines.Add(key, pipeline);
            }
            VulkanDevice.Commands.SetPipeline(pipeline);
            if (_textured && texture is null) throw new InvalidOperationException("The textured Vulkan program requires a bound texture.");
            texture ??= VulkanDevice.CreateFallbackTexture();
            if (!_resourceSets.TryGetValue(texture, out var resourceSet))
            {
                resourceSet = VulkanDevice.Factory.CreateResourceSet(new ResourceSetDescription(
                    _uniformLayout, _uniformBuffer, texture.View, texture.Sampler));
                _resourceSets.Add(texture, resourceSet);
                texture.Disposing += OnTextureDisposing;
            }
            VulkanDevice.Commands.SetGraphicsResourceSet(0, resourceSet);
        }

        private void OnTextureDisposing(VulkanTexture2D texture)
        {
            texture.Disposing -= OnTextureDisposing;
            if (_resourceSets.Remove(texture, out var resourceSet)) resourceSet.Dispose();
        }

        private Pipeline CreatePipeline(VulkanMesh mesh, PipelineKey key)
        {
            var elements = mesh.LayoutUnchecked.Attributes.OrderBy(attribute => attribute.Location)
                .Select(attribute => new VertexElementDescription(
                    $"Attribute{attribute.Location}",
                    VertexElementSemantic.TextureCoordinate,
                    attribute.ComponentCount switch
                    {
                        1 => VertexElementFormat.Float1,
                        2 => VertexElementFormat.Float2,
                        3 => VertexElementFormat.Float3,
                        4 => VertexElementFormat.Float4,
                        _ => throw new ArgumentOutOfRangeException()
                    })).ToArray();
            var vertexLayout = new VertexLayoutDescription((uint)mesh.LayoutUnchecked.StrideBytes, 0, elements);
            var shaderSet = new ShaderSetDescription([vertexLayout], _shaders);
            var blend = key.BlendMode == GraphicsBlendMode.AlphaBlend
                ? BlendStateDescription.SingleAlphaBlend : BlendStateDescription.SingleOverrideBlend;
            var depth = new DepthStencilStateDescription(
                key.DepthState.TestEnabled,
                key.DepthState.WriteEnabled,
                ComparisonKind.LessEqual);
            var rasterizer = new RasterizerStateDescription(
                key.RasterizerState.CullMode switch
                {
                    GraphicsCullMode.Back => FaceCullMode.Back,
                    GraphicsCullMode.Front => FaceCullMode.Front,
                    _ => FaceCullMode.None
                },
                PolygonFillMode.Solid,
                FrontFace.CounterClockwise,
                true,
                false);
            var description = new GraphicsPipelineDescription(
                blend,
                depth,
                rasterizer,
                mesh.TopologyUnchecked == GraphicsPrimitiveTopology.LineList
                    ? PrimitiveTopology.LineList : PrimitiveTopology.TriangleList,
                shaderSet,
                [_uniformLayout],
                VulkanDevice.NativeDevice.MainSwapchain.Framebuffer.OutputDescription);
            return VulkanDevice.Factory.CreateGraphicsPipeline(description);
        }

        public override void Dispose()
        {
            if (_disposed) return;
            foreach (var (texture, resourceSet) in _resourceSets)
            {
                texture.Disposing -= OnTextureDisposing;
                resourceSet.Dispose();
            }
            _resourceSets.Clear();
            foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
            _pipelines.Clear();
            _uniformLayout.Dispose();
            _uniformBuffer.Dispose();
            foreach (var shader in _shaders) shader.Dispose();
            _disposed = true;
        }

        private readonly record struct PipelineKey(
            GraphicsVertexLayout Layout,
            GraphicsPrimitiveTopology Topology,
            GraphicsBlendMode BlendMode,
            GraphicsDepthState DepthState,
            GraphicsRasterizerState RasterizerState);
    }

    private VulkanTexture2D? _fallbackTexture;
    private VulkanTexture2D CreateFallbackTexture() => _fallbackTexture ??= new VulkanTexture2D(
        this,
        "BEngine.Vulkan.White",
        new GraphicsTextureDescription(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled),
        [255, 255, 255, 255]);
}
