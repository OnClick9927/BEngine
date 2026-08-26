using Silk.NET.OpenGL;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi.OpenGL;

public sealed class OpenGlGraphicsDevice : IGraphicsDevice, IGraphicsDeviceStatistics
{
    private const GraphicsDeviceFeatures SupportedFeatures =
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

    private readonly GL _api;
    private readonly GraphicsDeviceCapabilities _capabilities;
    private uint _emptyVertexArray;
    private GraphicsRect _viewport;
    private GraphicsDrawStatistics _drawStatistics;
    private bool _disposed;

    public GraphicsBackend Backend
    {
        get { return GraphicsBackend.OpenGL; }
    }
    public GraphicsDeviceCapabilities Capabilities
    {
        get { return _capabilities; }
    }
    public GraphicsDrawStatistics DrawStatistics => _drawStatistics;

    internal GL Api => _api;

    public OpenGlGraphicsDevice(GL api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _capabilities = new GraphicsDeviceCapabilities(
            GraphicsBackend.OpenGL,
            "Host OpenGL context",
            "OpenGL 3.3 core contract",
            SupportedFeatures,
            [GraphicsShaderLanguage.Glsl]);
        GraphicsBackendSettings.ReportActive(_capabilities);
    }

    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        description.Validate();
        if (description.VertexShader.Language != GraphicsShaderLanguage.Glsl)
            throw new NotSupportedException("The OpenGL provider accepts GLSL shader source.");
        if (!string.Equals(description.VertexShader.EntryPoint, "main", StringComparison.Ordinal) ||
            !string.Equals(description.FragmentShader.EntryPoint, "main", StringComparison.Ordinal))
            throw new NotSupportedException("The OpenGL provider currently requires a 'main' shader entry point.");
        return new OpenGlProgram(this, description);
    }

    public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        return new OpenGlMesh(this, description);
    }

    public IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        return new OpenGlTexture2D(this, label, description, initialData);
    }

    public IGraphicsRenderTarget CreateRenderTarget(
        string label,
        GraphicsRenderTargetDescription description)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        return new OpenGlRenderTarget(this, label, description);
    }

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
    {
        ThrowIfDisposed();
        var target = RequireResource<OpenGlRenderTarget>(renderTarget);
        _api.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
        _api.BindFramebuffer(FramebufferTarget.Framebuffer, target.Handle);
        return new RenderTargetScope(this, (uint)previousFramebuffer);
    }

    public void SetViewport(GraphicsRect viewport)
    {
        ThrowIfDisposed();
        viewport.Validate();
        _viewport = viewport;
        _api.Viewport(viewport.X, viewport.Y, (uint)viewport.Width, (uint)viewport.Height);
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        ThrowIfDisposed();
        if (scissor is null)
        {
            _api.Disable(EnableCap.ScissorTest);
            return;
        }

        scissor.Value.Validate();
        _api.Enable(EnableCap.ScissorTest);
        var x = _viewport.X + scissor.Value.X;
        var y = _viewport.Y + _viewport.Height - scissor.Value.Y - scissor.Value.Height;
        _api.Scissor(x, y,
            (uint)scissor.Value.Width, (uint)scissor.Value.Height);
    }

    public void Clear(GraphicsClearFlags flags, NumericsVector4 color)
    {
        ThrowIfDisposed();
        var mask = (ClearBufferMask)0;
        if ((flags & GraphicsClearFlags.Color) != 0)
        {
            _api.ClearColor(color.X, color.Y, color.Z, color.W);
            mask |= ClearBufferMask.ColorBufferBit;
        }
        if ((flags & GraphicsClearFlags.Depth) != 0) mask |= ClearBufferMask.DepthBufferBit;
        if ((flags & GraphicsClearFlags.Stencil) != 0) mask |= ClearBufferMask.StencilBufferBit;
        if (mask != 0) _api.Clear(mask);
    }

    public void SetDepthState(GraphicsDepthState state)
    {
        ThrowIfDisposed();
        if (state.TestEnabled) _api.Enable(EnableCap.DepthTest);
        else _api.Disable(EnableCap.DepthTest);
        _api.DepthMask(state.WriteEnabled);
    }

    public void SetBlendMode(GraphicsBlendMode mode)
    {
        ThrowIfDisposed();
        switch (mode)
        {
            case GraphicsBlendMode.Disabled:
                _api.Disable(EnableCap.Blend);
                break;
            case GraphicsBlendMode.AlphaBlend:
                _api.Enable(EnableCap.Blend);
                _api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    public void SetRasterizerState(GraphicsRasterizerState state)
    {
        ThrowIfDisposed();
        if (state.CullMode == GraphicsCullMode.None)
            _api.Disable(EnableCap.CullFace);
        else
        {
            _api.Enable(EnableCap.CullFace);
            _api.CullFace(state.CullMode == GraphicsCullMode.Back
                ? TriangleFace.Back
                : TriangleFace.Front);
            _api.FrontFace(FrontFaceDirection.Ccw);
        }
        if (state.DepthBiasEnabled)
        {
            _api.Enable(EnableCap.PolygonOffsetFill);
            _api.PolygonOffset(state.SlopeScale, state.ConstantBias);
        }
        else
            _api.Disable(EnableCap.PolygonOffsetFill);
    }

    public void BindTexture(int slot, IGraphicsTexture2D texture)
    {
        ThrowIfDisposed();
        if (slot is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(slot));
        var openGlTexture = RequireResource<OpenGlTexture2D>(texture);
        if ((openGlTexture.DescriptionUnchecked.Usage & GraphicsTextureUsage.Sampled) == 0)
            throw new InvalidOperationException($"Texture '{texture.Label}' was not created for sampling.");
        _api.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + (uint)slot));
        _api.BindTexture(TextureTarget.Texture2D, openGlTexture.Handle);
    }

    public void Draw(IGraphicsMesh mesh)
    {
        ThrowIfDisposed();
        var openGlMesh = RequireResource<OpenGlMesh>(mesh);
        _api.BindVertexArray(openGlMesh.VertexArray);
        _api.DrawArrays(ToOpenGl(openGlMesh.TopologyUnchecked), 0, (uint)openGlMesh.VertexCountUnchecked);
        _drawStatistics = _drawStatistics.AddDraw(
            openGlMesh.VertexCountUnchecked, openGlMesh.TopologyUnchecked);
    }

    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
    {
        ThrowIfDisposed();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        var openGlMesh = RequireResource<OpenGlMesh>(mesh);
        if (firstVertex + vertexCount > openGlMesh.VertexCountUnchecked)
            throw new ArgumentOutOfRangeException(nameof(vertexCount), "The draw range exceeds the mesh vertex count.");
        _api.BindVertexArray(openGlMesh.VertexArray);
        _api.DrawArrays(ToOpenGl(openGlMesh.TopologyUnchecked), firstVertex, (uint)vertexCount);
        _drawStatistics = _drawStatistics.AddDraw(vertexCount, openGlMesh.TopologyUnchecked);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        ThrowIfDisposed();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        if (_emptyVertexArray == 0) _emptyVertexArray = _api.GenVertexArray();
        _api.BindVertexArray(_emptyVertexArray);
        _api.DrawArrays(ToOpenGl(topology), firstVertex, (uint)vertexCount);
        _drawStatistics = _drawStatistics.AddDraw(vertexCount, topology);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_emptyVertexArray != 0) _api.DeleteVertexArray(_emptyVertexArray);
        _emptyVertexArray = 0;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private T RequireResource<T>(IGraphicsResource resource) where T : OpenGlResource
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is not T typedResource)
            throw new ArgumentException("The graphics resource is not compatible with the OpenGL provider.",
                nameof(resource));
        if (!ReferenceEquals(typedResource.Owner, this))
            throw new ArgumentException("The graphics resource belongs to another device.", nameof(resource));
        typedResource.ThrowIfDisposed();
        return typedResource;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static PrimitiveType ToOpenGl(GraphicsPrimitiveTopology topology) => topology switch
    {
        GraphicsPrimitiveTopology.TriangleList => PrimitiveType.Triangles,
        GraphicsPrimitiveTopology.LineList => PrimitiveType.Lines,
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

    private abstract class OpenGlResource : IGraphicsResource
    {
        private bool _disposed;

        protected OpenGlGraphicsDevice OpenGlDevice { get; }
        protected GL Gl => OpenGlDevice._api;
        internal OpenGlGraphicsDevice Owner => OpenGlDevice;
        private readonly string _label;
        public IGraphicsDevice Device
        {
            get { return OpenGlDevice; }
        }
        public string Label
        {
            get { return _label; }
        }
        protected bool IsDisposed => _disposed;

        protected OpenGlResource(OpenGlGraphicsDevice device, string label)
        {
            OpenGlDevice = device;
            _label = label;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Release();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        protected abstract void Release();
    }

    private sealed class OpenGlProgram : OpenGlResource, IGraphicsProgram
    {
        private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);
        private uint _handle;

        public OpenGlProgram(OpenGlGraphicsDevice device, GraphicsShaderProgramDescription description)
            : base(device, description.Label)
        {
            var vertex = Compile(ShaderType.VertexShader, description.VertexShader.Code);
            uint fragment = 0;
            try
            {
                fragment = Compile(ShaderType.FragmentShader, description.FragmentShader.Code);
                _handle = Gl.CreateProgram();
                Gl.AttachShader(_handle, vertex);
                Gl.AttachShader(_handle, fragment);
                Gl.LinkProgram(_handle);
                Gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out var status);
                if (status == 0)
                {
                    var log = Gl.GetProgramInfoLog(_handle);
                    throw new InvalidOperationException($"OpenGL program '{Label}' failed to link: {log}");
                }
            }
            catch
            {
                if (_handle != 0) Gl.DeleteProgram(_handle);
                _handle = 0;
                throw;
            }
            finally
            {
                if (_handle != 0)
                {
                    Gl.DetachShader(_handle, vertex);
                    if (fragment != 0) Gl.DetachShader(_handle, fragment);
                }
                Gl.DeleteShader(vertex);
                if (fragment != 0) Gl.DeleteShader(fragment);
            }
        }

        public void Bind()
        {
            ThrowIfDisposed();
            Gl.UseProgram(_handle);
        }

        public unsafe void SetMatrix4x4(string name, NumericsMatrix4x4 value)
        {
            ThrowIfDisposed();
            Gl.UniformMatrix4(GetUniformLocation(name), 1, false, (float*)&value);
        }

        public void SetVector4(string name, NumericsVector4 value)
        {
            ThrowIfDisposed();
            Gl.Uniform4(GetUniformLocation(name), value.X, value.Y, value.Z, value.W);
        }

        public void SetFloat(string name, float value)
        {
            ThrowIfDisposed();
            Gl.Uniform1(GetUniformLocation(name), value);
        }

        public void SetInt(string name, int value)
        {
            ThrowIfDisposed();
            Gl.Uniform1(GetUniformLocation(name), value);
        }

        protected override void Release()
        {
            if (_handle != 0) Gl.DeleteProgram(_handle);
            _handle = 0;
            _uniformLocations.Clear();
        }

        private uint Compile(ShaderType type, string source)
        {
            var shader = Gl.CreateShader(type);
            try
            {
                Gl.ShaderSource(shader, source);
                Gl.CompileShader(shader);
                Gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
                if (status != 0) return shader;
                var log = Gl.GetShaderInfoLog(shader);
                throw new InvalidOperationException($"OpenGL shader for '{Label}' failed to compile: {log}");
            }
            catch
            {
                Gl.DeleteShader(shader);
                throw;
            }
        }

        private int GetUniformLocation(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (_uniformLocations.TryGetValue(name, out var location)) return location;
            location = Gl.GetUniformLocation(_handle, name);
            _uniformLocations.Add(name, location);
            return location;
        }
    }

    private sealed class OpenGlMesh : OpenGlResource, IGraphicsMesh
    {
        private uint _vertexBuffer;
        private readonly GraphicsVertexLayout _layout;
        private readonly GraphicsPrimitiveTopology _topology;
        private readonly GraphicsBufferUsage _usage;
        private int _vertexCount;

        public uint VertexArray { get; private set; }
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
        internal GraphicsPrimitiveTopology TopologyUnchecked => _topology;
        internal int VertexCountUnchecked => _vertexCount;

        public unsafe OpenGlMesh(OpenGlGraphicsDevice device, GraphicsMeshDescription description)
            : base(device, description.Label)
        {
            _layout = description.Layout;
            _topology = description.Topology;
            _usage = description.Usage;
            VertexArray = Gl.GenVertexArray();
            _vertexBuffer = Gl.GenBuffer();
            Gl.BindVertexArray(VertexArray);
            Gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            Upload(description.Vertices.Span, _usage);
            foreach (var attribute in _layout.Attributes)
            {
                Gl.EnableVertexAttribArray((uint)attribute.Location);
                Gl.VertexAttribPointer(
                    (uint)attribute.Location,
                    attribute.ComponentCount,
                    VertexAttribPointerType.Float,
                    false,
                    (uint)_layout.StrideBytes,
                    (void*)attribute.OffsetBytes);
            }
            Gl.BindVertexArray(0);
        }

        public void Update(ReadOnlySpan<float> vertices)
        {
            ThrowIfDisposed();
            if (_usage != GraphicsBufferUsage.Dynamic)
                throw new InvalidOperationException($"Mesh '{Label}' was created as a static vertex buffer.");
            if (vertices.Length * sizeof(float) % _layout.StrideBytes != 0)
                throw new ArgumentException("Vertex data length must be a multiple of the layout stride.",
                    nameof(vertices));
            Gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            Upload(vertices, GraphicsBufferUsage.Dynamic);
        }

        protected override void Release()
        {
            if (_vertexBuffer != 0) Gl.DeleteBuffer(_vertexBuffer);
            if (VertexArray != 0) Gl.DeleteVertexArray(VertexArray);
            _vertexBuffer = 0;
            VertexArray = 0;
            _vertexCount = 0;
        }

        private unsafe void Upload(ReadOnlySpan<float> vertices, GraphicsBufferUsage usage)
        {
            fixed (float* pointer = vertices)
            {
                Gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * sizeof(float)),
                    pointer,
                    usage == GraphicsBufferUsage.Dynamic ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw);
            }
            _vertexCount = vertices.Length * sizeof(float) / _layout.StrideBytes;
        }
    }

    private sealed class OpenGlTexture2D : OpenGlResource, IGraphicsTexture2D
    {
        private readonly GraphicsTextureDescription _description;
        public uint Handle { get; private set; }
        public GraphicsTextureDescription Description
        {
            get { return _description; }
        }
        internal GraphicsTextureDescription DescriptionUnchecked => _description;

        public OpenGlTexture2D(
            OpenGlGraphicsDevice device,
            string label,
            GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData)
            : base(device, label)
        {
            _description = description;
            Handle = Gl.GenTexture();
            Gl.BindTexture(TextureTarget.Texture2D, Handle);
            Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)ToOpenGl(description.MinFilter));
            Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)ToOpenGl(description.MagFilter));
            Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)ToOpenGl(description.AddressMode));
            Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)ToOpenGl(description.AddressMode));
            Allocate(initialData);
        }

        public unsafe void Update(ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed();
            ValidatePixelData(pixels);
            var (_, format, type, _) = GetFormat(_description.Format);
            Gl.BindTexture(TextureTarget.Texture2D, Handle);
            Gl.GetInteger(GetPName.UnpackAlignment, out var previousAlignment);
            Gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            try
            {
                fixed (byte* pointer = pixels)
                {
                    Gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                        (uint)_description.Width, (uint)_description.Height, format, type, pointer);
                }
            }
            finally
            {
                Gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment);
            }
        }

        protected override void Release()
        {
            if (Handle != 0) Gl.DeleteTexture(Handle);
            Handle = 0;
        }

        private unsafe void Allocate(ReadOnlySpan<byte> initialData)
        {
            if (!initialData.IsEmpty) ValidatePixelData(initialData);
            var (internalFormat, format, type, _) = GetFormat(_description.Format);
            Gl.GetInteger(GetPName.UnpackAlignment, out var previousAlignment);
            Gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            try
            {
                fixed (byte* pointer = initialData)
                {
                    Gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                        (uint)_description.Width, (uint)_description.Height, 0, format, type,
                        initialData.IsEmpty ? null : pointer);
                }
            }
            finally
            {
                Gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment);
            }
        }

        private void ValidatePixelData(ReadOnlySpan<byte> pixels)
        {
            var (_, _, _, bytesPerPixel) = GetFormat(_description.Format);
            if (bytesPerPixel == 0)
                throw new NotSupportedException($"CPU upload for {_description.Format} textures is not supported.");
            var expectedLength = checked(_description.Width * _description.Height * bytesPerPixel);
            if (pixels.Length != expectedLength)
                throw new ArgumentException($"Texture '{Label}' requires {expectedLength} bytes, got {pixels.Length}.",
                    nameof(pixels));
        }

        private static (InternalFormat Internal, PixelFormat Format, PixelType Type, int BytesPerPixel)
            GetFormat(GraphicsTextureFormat format) => format switch
            {
                GraphicsTextureFormat.R8Unorm =>
                    (InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte, 1),
                GraphicsTextureFormat.Rgba8Unorm =>
                    (InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, 4),
                GraphicsTextureFormat.Depth24Unorm =>
                    (InternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.Float, 0),
                GraphicsTextureFormat.Depth24Stencil8 =>
                    (InternalFormat.Depth24Stencil8, PixelFormat.DepthStencil, PixelType.UnsignedInt248, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

        private static GLEnum ToOpenGl(GraphicsTextureFilter filter) => filter switch
        {
            GraphicsTextureFilter.Nearest => GLEnum.Nearest,
            GraphicsTextureFilter.Linear => GLEnum.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        private static GLEnum ToOpenGl(GraphicsTextureAddressMode addressMode) => addressMode switch
        {
            GraphicsTextureAddressMode.ClampToEdge => GLEnum.ClampToEdge,
            GraphicsTextureAddressMode.Repeat => GLEnum.Repeat,
            GraphicsTextureAddressMode.MirroredRepeat => GLEnum.MirroredRepeat,
            _ => throw new ArgumentOutOfRangeException(nameof(addressMode))
        };
    }

    private sealed class OpenGlRenderTarget : OpenGlResource, IGraphicsRenderTarget
    {
        private readonly OpenGlTexture2D? _colorTexture;
        private readonly OpenGlTexture2D? _depthTexture;
        private readonly int _width;
        private readonly int _height;

        public uint Handle { get; private set; }
        public int Width
        {
            get { return _width; }
        }
        public int Height
        {
            get { return _height; }
        }
        public IGraphicsTexture2D? ColorTexture
        {
            get { return _colorTexture; }
        }
        public IGraphicsTexture2D? DepthTexture
        {
            get { return _depthTexture; }
        }

        public OpenGlRenderTarget(
            OpenGlGraphicsDevice device,
            string label,
            GraphicsRenderTargetDescription description)
            : base(device, label)
        {
            _width = description.Width;
            _height = description.Height;
            Gl.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
            Gl.GetInteger(GetPName.TextureBinding2D, out var previousTexture);
            try
            {
                Handle = Gl.GenFramebuffer();
                Gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
                if (description.ColorFormat is { } colorFormat)
                {
                    _colorTexture = CreateAttachment(
                        $"{label}.Color",
                        colorFormat,
                        description.SampleColor,
                        description.Filter);
                    Gl.FramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0,
                        TextureTarget.Texture2D,
                        _colorTexture.Handle,
                        0);
                    Gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
                    Gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
                }
                else
                {
                    Gl.DrawBuffer(DrawBufferMode.None);
                    Gl.ReadBuffer(ReadBufferMode.None);
                }

                if (description.DepthFormat is { } depthFormat)
                {
                    _depthTexture = CreateAttachment(
                        $"{label}.Depth",
                        depthFormat,
                        description.SampleDepth,
                        description.Filter);
                    var attachment = depthFormat == GraphicsTextureFormat.Depth24Stencil8
                        ? FramebufferAttachment.DepthStencilAttachment
                        : FramebufferAttachment.DepthAttachment;
                    Gl.FramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        attachment,
                        TextureTarget.Texture2D,
                        _depthTexture.Handle,
                        0);
                }

                var status = Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status != GLEnum.FramebufferComplete)
                    throw new InvalidOperationException($"OpenGL render target '{label}' is incomplete: {status}.");
            }
            catch
            {
                _depthTexture?.Dispose();
                _colorTexture?.Dispose();
                if (Handle != 0) Gl.DeleteFramebuffer(Handle);
                Handle = 0;
                throw;
            }
            finally
            {
                Gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture);
                Gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
            }
        }

        protected override void Release()
        {
            _depthTexture?.Dispose();
            _colorTexture?.Dispose();
            if (Handle != 0) Gl.DeleteFramebuffer(Handle);
            Handle = 0;
        }

        private OpenGlTexture2D CreateAttachment(
            string label,
            GraphicsTextureFormat format,
            bool sampled,
            GraphicsTextureFilter filter)
        {
            var usage = GraphicsTextureUsage.RenderTarget;
            if (sampled) usage |= GraphicsTextureUsage.Sampled;
            return new OpenGlTexture2D(
                OpenGlDevice,
                label,
                new GraphicsTextureDescription(
                    _width,
                    _height,
                    format,
                    usage,
                    filter,
                    filter,
                    GraphicsTextureAddressMode.ClampToEdge),
                default);
        }
    }

    private sealed class RenderTargetScope : IDisposable
    {
        private OpenGlGraphicsDevice? _device;
        private readonly uint _previousFramebuffer;

        public RenderTargetScope(OpenGlGraphicsDevice device, uint previousFramebuffer)
        {
            _device = device;
            _previousFramebuffer = previousFramebuffer;
        }

        public void Dispose()
        {
            var device = _device;
            _device = null;
            if (device is null) return;
            device.ThrowIfDisposed();
            device._api.BindFramebuffer(FramebufferTarget.Framebuffer, _previousFramebuffer);
        }
    }
}
