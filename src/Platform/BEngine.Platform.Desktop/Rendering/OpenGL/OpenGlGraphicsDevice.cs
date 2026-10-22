using Silk.NET.OpenGL;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public sealed class OpenGlGraphicsDevice : IGraphicsDevice, IGraphicsDeviceStatistics, IGraphicsColorReadback, IGraphicsTextureFormatSupport
    , IGraphicsFrameTimingSource, IGraphicsPassTimingSource, IGraphicsStateCache, IGraphicsRenderTargetScopeDevice
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
    private readonly List<(long Token, uint Framebuffer)> _targetScopes = new(4);
    private long _targetScopeSequence;
    private GraphicsDrawStatistics _drawStatistics;
    private bool _disposed;
    private uint _boundProgram = uint.MaxValue, _boundVertexArray = uint.MaxValue;
    private readonly uint[] _boundTextures = new uint[32];
    private int _activeTextureSlot = -1;
    private long _stateGeneration;
    private long _programBinds, _uniformWrites, _textureBinds, _vertexArrayBinds, _bindingCacheHits;
    public GraphicsBindingStatistics BindingStatistics => new(_programBinds, _uniformWrites, _textureBinds, _vertexArrayBinds, _bindingCacheHits);
    public void InvalidateState() { ThrowIfDisposed(); ResetState(); }
    private void ResetState()
    {
        _boundProgram = _boundVertexArray = uint.MaxValue; _activeTextureSlot = -1;
        Array.Fill(_boundTextures, uint.MaxValue); _stateGeneration++;
    }
    private void UseProgram(uint handle)
    {
        if (_boundProgram == handle) { _bindingCacheHits++; return; }
        _api.UseProgram(handle); _boundProgram = handle; _programBinds++;
    }
    private void BindVertexArray(uint handle)
    {
        if (_boundVertexArray == handle) { _bindingCacheHits++; return; }
        _api.BindVertexArray(handle); _boundVertexArray = handle; _vertexArrayBinds++;
    }
    private readonly Queue<uint> _pendingTimingQueries = new();
    private uint _activeTimingQuery;
    private long _passFrame;
    private readonly Queue<PassQuery> _pendingPassQueries = new();
    private readonly HashSet<PassQuery> _activePassQueries = [];

    public GraphicsBackend Backend
    {
        get { return GraphicsBackend.OpenGL; }
    }
    public GraphicsDeviceCapabilities Capabilities
    {
        get { return _capabilities; }
    }
    public GraphicsDrawStatistics DrawStatistics => _drawStatistics;
    public bool SupportsGpuFrameTiming => true;
    public GraphicsColorReadbackCapabilities ColorReadbackCapabilities => new(
        true,
        GraphicsColorReadbackValidation.DefaultMaximumBytes);

    internal GL Api => _api;

    public OpenGlGraphicsDevice(GL api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        ResetState();
        _capabilities = new GraphicsDeviceCapabilities(
            GraphicsBackend.OpenGL,
            api.GetStringS(StringName.Renderer),
            api.GetStringS(StringName.Version),
            SupportedFeatures,
            [GraphicsShaderLanguage.Glsl])
        {
            DeviceVendor = api.GetStringS(StringName.Vendor),
            MaxTextureSize = api.GetInteger(GetPName.MaxTextureSize)
        };
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
        _boundVertexArray = uint.MaxValue;
        return new OpenGlMesh(this, description);
    }

    public bool SupportsTextureFormat(GraphicsTextureFormat format)
    {
        ThrowIfDisposed();
        return format switch
        {
            GraphicsTextureFormat.Bc1RgbaUnorm or GraphicsTextureFormat.Bc3RgbaUnorm =>
                _api.IsExtensionPresent("GL_EXT_texture_compression_s3tc"),
            GraphicsTextureFormat.Bc7RgbaUnorm => _api.IsExtensionPresent("GL_ARB_texture_compression_bptc"),
            GraphicsTextureFormat.Etc2Rgba8Unorm => _api.IsExtensionPresent("GL_ARB_ES3_compatibility"),
            GraphicsTextureFormat.Astc4x4Unorm => _api.IsExtensionPresent("GL_KHR_texture_compression_astc_ldr"),
            _ => format is GraphicsTextureFormat.R8Unorm or GraphicsTextureFormat.Rgba8Unorm or
                GraphicsTextureFormat.Depth24Unorm or GraphicsTextureFormat.Depth24Stencil8
        };
    }

    public IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        if (!SupportsTextureFormat(description.Format)) throw new NotSupportedException($"Device does not support {description.Format}.");
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

    public long PushRenderTargetState(IGraphicsRenderTarget renderTarget)
    {
        ThrowIfDisposed();
        var target = RequireResource<OpenGlRenderTarget>(renderTarget);
        _api.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
        var token = checked(_targetScopeSequence + 1);
        _targetScopes.Add((token, (uint)previousFramebuffer));
        _targetScopeSequence = token;
        _api.BindFramebuffer(FramebufferTarget.Framebuffer, target.Handle);
        return token;
    }

    public void PopRenderTargetState(long token)
    {
        if (token <= 0 || token > _targetScopeSequence) throw new ArgumentOutOfRangeException(nameof(token));
        var last = _targetScopes.Count - 1;
        if (last < 0 || _targetScopes[last].Token != token)
        {
            foreach (var state in _targetScopes)
                if (state.Token == token)
                    throw new InvalidOperationException("Render target scopes must be disposed in reverse order.");
            return;
        }
        ThrowIfDisposed();
        var previous = _targetScopes[last].Framebuffer;
        _targetScopes.RemoveAt(last);
        _api.BindFramebuffer(FramebufferTarget.Framebuffer, previous);
    }

    public unsafe GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region,
        int surfaceWidth,
        int surfaceHeight)
    {
        ThrowIfDisposed();
        GraphicsColorReadbackValidation.ValidateRegion(
            region,
            surfaceWidth,
            surfaceHeight,
            GraphicsColorReadbackValidation.DefaultMaximumBytes);

        var pixels = new byte[checked(region.Width * region.Height * 4)];
        _api.GetInteger(GetPName.PackAlignment, out var previousAlignment);
        try
        {
            _api.PixelStore(PixelStoreParameter.PackAlignment, 1);
            var framebufferRegion = ToFramebufferReadRegion(region, surfaceHeight);
            fixed (byte* pointer = pixels)
                _api.ReadPixels(framebufferRegion.X, framebufferRegion.Y,
                    (uint)framebufferRegion.Width, (uint)framebufferRegion.Height,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pointer);
            FlipRowsInPlace(pixels, region.Width, region.Height);
            return GraphicsColorReadbackRequest.Ready(
                region,
                GraphicsColorReadbackImage.FromOwnedRgba8(region.Width, region.Height, pixels));
        }
        catch (Exception exception)
        {
            return GraphicsColorReadbackRequest.Failed(region, exception.Message);
        }
        finally
        {
            _api.PixelStore(PixelStoreParameter.PackAlignment, previousAlignment);
        }
    }

    internal static GraphicsRect ToFramebufferReadRegion(GraphicsRect region, int surfaceHeight) =>
        new(
            region.X,
            surfaceHeight - region.Y - region.Height,
            region.Width,
            region.Height);

    internal static void FlipRowsInPlace(byte[] pixels, int width, int height)
    {
        var rowBytes = checked(width * 4);
        var temporary = new byte[rowBytes];
        for (var top = 0; top < height / 2; top++)
        {
            var bottom = height - top - 1;
            var topRow = pixels.AsSpan(top * rowBytes, rowBytes);
            var bottomRow = pixels.AsSpan(bottom * rowBytes, rowBytes);
            topRow.CopyTo(temporary);
            bottomRow.CopyTo(topRow);
            temporary.CopyTo(bottomRow);
        }
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
                _api.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
                    BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
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
        if (_boundTextures[slot] == openGlTexture.Handle) { _bindingCacheHits++; return; }
        if (_activeTextureSlot != slot)
        {
            _api.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + (uint)slot));
            _activeTextureSlot = slot;
        }
        _api.BindTexture(TextureTarget.Texture2D, openGlTexture.Handle);
        _boundTextures[slot] = openGlTexture.Handle; _textureBinds++;
    }

    public void Draw(IGraphicsMesh mesh)
    {
        ThrowIfDisposed();
        var openGlMesh = RequireResource<OpenGlMesh>(mesh);
        BindVertexArray(openGlMesh.VertexArray);
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
        BindVertexArray(openGlMesh.VertexArray);
        _api.DrawArrays(ToOpenGl(openGlMesh.TopologyUnchecked), firstVertex, (uint)vertexCount);
        _drawStatistics = _drawStatistics.AddDraw(vertexCount, openGlMesh.TopologyUnchecked);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        ThrowIfDisposed();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        if (_emptyVertexArray == 0) _emptyVertexArray = _api.GenVertexArray();
        BindVertexArray(_emptyVertexArray);
        _api.DrawArrays(ToOpenGl(topology), firstVertex, (uint)vertexCount);
        _drawStatistics = _drawStatistics.AddDraw(vertexCount, topology);
    }

    public void BeginGpuFrameTiming()
    {
        ThrowIfDisposed();
        _passFrame++; CompletePassQueries();
        CompleteTimingQueries();
        if (!GraphicsFrameTimingService.IsEnabled || _activeTimingQuery != 0 || _pendingTimingQueries.Count >= 8) return;
        _activeTimingQuery = _api.GenQuery();
        _api.BeginQuery(QueryTarget.TimeElapsed, _activeTimingQuery);
    }

    public void EndGpuFrameTiming()
    {
        ThrowIfDisposed();
        if (_activeTimingQuery == 0) return;
        _api.EndQuery(QueryTarget.TimeElapsed);
        _pendingTimingQueries.Enqueue(_activeTimingQuery);
        _activeTimingQuery = 0;
        CompleteTimingQueries();
        CompletePassQueries();
    }

    public IDisposable? BeginGpuPass(string name)
    {
        ThrowIfDisposed();
        if (!GraphicsFrameTimingService.IsEnabled) return null;
        if (_activePassQueries.Count + _pendingPassQueries.Count >= 512) return null;
        var query = new PassQuery(this, _passFrame, name, _api.GenQuery(), _api.GenQuery());
        _activePassQueries.Add(query); _api.QueryCounter(query.Start, QueryCounterTarget.Timestamp); return query;
    }
    private void CompletePassQueries()
    {
        while (_pendingPassQueries.TryPeek(out var query))
        {
            _api.GetQueryObject(query.End, QueryObjectParameterName.ResultAvailable, out int ready); if (ready == 0) break;
            _api.GetQueryObject(query.Start, QueryObjectParameterName.Result, out ulong start);
            _api.GetQueryObject(query.End, QueryObjectParameterName.Result, out ulong end);
            _pendingPassQueries.Dequeue(); query.Delete();
            GraphicsPassTimingService.Report(new(query.Frame, Backend, query.Name, (end - start) / 1_000_000d));
        }
    }
    private sealed class PassQuery(OpenGlGraphicsDevice owner, long frame, string name, uint start, uint end) : IDisposable
    {
        private bool _ended;
        internal long Frame => frame; internal string Name => name; internal uint Start => start; internal uint End => end;
        public void Dispose()
        {
            if (_ended || owner._disposed) return; _ended = true; owner._activePassQueries.Remove(this);
            owner._api.QueryCounter(End, QueryCounterTarget.Timestamp); owner._pendingPassQueries.Enqueue(this);
        }
        internal void Delete() { _ended = true; owner._api.DeleteQuery(Start); owner._api.DeleteQuery(End); }
    }

    private void CompleteTimingQueries()
    {
        while (_pendingTimingQueries.TryPeek(out var query))
        {
            _api.GetQueryObject(query, QueryObjectParameterName.ResultAvailable, out int available);
            if (available == 0) break;
            _api.GetQueryObject(query, QueryObjectParameterName.Result, out ulong nanoseconds);
            _pendingTimingQueries.Dequeue();
            _api.DeleteQuery(query);
            GraphicsFrameTimingService.Report(
                GraphicsBackend.OpenGL,
                nanoseconds / 1_000_000d,
                "timer-query");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var query in _activePassQueries) query.Delete(); _activePassQueries.Clear();
        while (_pendingPassQueries.TryDequeue(out var passQuery)) passQuery.Delete();
        if (_activeTimingQuery != 0)
        {
            _api.EndQuery(QueryTarget.TimeElapsed);
            _api.DeleteQuery(_activeTimingQuery);
            _activeTimingQuery = 0;
        }
        while (_pendingTimingQueries.TryDequeue(out var timingQuery)) _api.DeleteQuery(timingQuery);
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
            OpenGlDevice.ResetState();
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
        private readonly Dictionary<int, (long Generation, int Bits, bool Integer)> _scalarUniforms = [];
        private readonly Dictionary<int, (long Generation, NumericsVector4 Value)> _vectorUniforms = [];

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
            OpenGlDevice.UseProgram(_handle);
        }

        public unsafe void SetMatrix4x4(string name, NumericsMatrix4x4 value)
        {
            ThrowIfDisposed();
            Bind();
            Gl.UniformMatrix4(GetUniformLocation(name), 1, false, (float*)&value);
            OpenGlDevice._uniformWrites++;
        }

        public void SetVector4(string name, NumericsVector4 value)
        {
            ThrowIfDisposed();
            Bind();
            var location = GetUniformLocation(name);
            if (_vectorUniforms.TryGetValue(location, out var cached) && cached.Generation == OpenGlDevice._stateGeneration && cached.Value == value)
            { OpenGlDevice._bindingCacheHits++; return; }
            Gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
            _vectorUniforms[location] = (OpenGlDevice._stateGeneration, value); OpenGlDevice._uniformWrites++;
        }

        public unsafe void SetVector4Array(string firstElementName, ReadOnlySpan<NumericsVector4> values)
        {
            ThrowIfDisposed();
            if (!firstElementName.EndsWith("[0]", StringComparison.Ordinal)) throw new ArgumentException("Expected an array [0] uniform.", nameof(firstElementName));
            if (values.IsEmpty) return;
            Bind();
            fixed (NumericsVector4* data = values) Gl.Uniform4(GetUniformLocation(firstElementName), (uint)values.Length, (float*)data);
            _vectorUniforms.Clear(); OpenGlDevice._uniformWrites++;
        }

        public void SetFloat(string name, float value)
        {
            ThrowIfDisposed();
            Bind();
            var bits = BitConverter.SingleToInt32Bits(value);
            var location = GetUniformLocation(name);
            if (_scalarUniforms.TryGetValue(location, out var cached) && cached.Generation == OpenGlDevice._stateGeneration && !cached.Integer && cached.Bits == bits)
            { OpenGlDevice._bindingCacheHits++; return; }
            Gl.Uniform1(location, value);
            _scalarUniforms[location] = (OpenGlDevice._stateGeneration, bits, false); OpenGlDevice._uniformWrites++;
        }

        public void SetInt(string name, int value)
        {
            ThrowIfDisposed();
            Bind();
            var location = GetUniformLocation(name);
            if (_scalarUniforms.TryGetValue(location, out var cached) && cached.Generation == OpenGlDevice._stateGeneration && cached.Integer && cached.Bits == value)
            { OpenGlDevice._bindingCacheHits++; return; }
            Gl.Uniform1(location, value);
            _scalarUniforms[location] = (OpenGlDevice._stateGeneration, value, true); OpenGlDevice._uniformWrites++;
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
        private BEngine.Diagnostics.NativeMemoryTracker.Allocation? _memory;
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
            _memory?.Dispose(); _memory = null;
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
            _memory ??= BEngine.Diagnostics.NativeMemoryTracker.Register("GPU.Buffer.OpenGL", Label, 0);
            _memory.Resize((long)vertices.Length * sizeof(float));
        }
    }

    private sealed class OpenGlTexture2D : OpenGlResource, IGraphicsTexture2D
    {
        private BEngine.Diagnostics.NativeMemoryTracker.Allocation? _memory;
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
            Gl.GetInteger(GetPName.TextureBinding2D, out var previousTexture);
            try
            {
                Gl.BindTexture(TextureTarget.Texture2D, Handle);
                Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)(description.MipLevels == 1 ? ToOpenGl(description.MinFilter) : description.Trilinear
                        ? GLEnum.LinearMipmapLinear : description.MinFilter == GraphicsTextureFilter.Nearest
                            ? GLEnum.NearestMipmapNearest : GLEnum.LinearMipmapNearest));
                Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, description.MipLevels - 1);
                Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)ToOpenGl(description.MagFilter));
                Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                    (int)ToOpenGl(description.AddressMode));
                Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                    (int)ToOpenGl(description.AddressMode));
                Allocate(initialData);
                _memory = BEngine.Diagnostics.NativeMemoryTracker.Register("GPU.Texture.OpenGL", label, description.DataByteSize);
            }
            catch { Gl.DeleteTexture(Handle); Handle = 0; throw; }
            finally { Gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture); }
        }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed(); ValidatePixelData(pixels);
            Gl.GetInteger(GetPName.TextureBinding2D, out var previousTexture);
            Gl.BindTexture(TextureTarget.Texture2D, Handle);
            try { Allocate(pixels); }
            finally { Gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture); }
        }

        protected override void Release()
        {
            if (Handle != 0) Gl.DeleteTexture(Handle);
            _memory?.Dispose(); _memory = null;
            Handle = 0;
        }

        private unsafe void Allocate(ReadOnlySpan<byte> initialData)
        {
            if (!initialData.IsEmpty) ValidatePixelData(initialData);
            if (_description.IsCompressed && initialData.IsEmpty)
                throw new ArgumentException("Compressed textures require initial data.");
            Gl.GetInteger(GetPName.UnpackAlignment, out var previousAlignment);
            Gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            try
            {
                fixed (byte* pointer = initialData)
                {
                    var offset = 0;
                    for (var level = 0; level < _description.MipLevels; level++)
                    {
                        var width = (uint)Math.Max(1, _description.Width >> level);
                        var height = (uint)Math.Max(1, _description.Height >> level);
                        var length = _description.LevelByteSize(level);
                        if (_description.IsCompressed)
                            Gl.CompressedTexImage2D(TextureTarget.Texture2D, level,
                                (InternalFormat)(_description.Format == GraphicsTextureFormat.Bc1RgbaUnorm ? 0x83F1 :
                                    _description.Format == GraphicsTextureFormat.Bc3RgbaUnorm ? 0x83F3 :
                                    _description.Format == GraphicsTextureFormat.Etc2Rgba8Unorm ? 0x9278 :
                                    _description.Format == GraphicsTextureFormat.Astc4x4Unorm ? 0x93B0 : 0x8E8C),
                                width, height, 0, (uint)length, pointer + offset);
                        else
                        {
                            var (internalFormat, format, type, _) = GetFormat(_description.Format);
                            Gl.TexImage2D(TextureTarget.Texture2D, level, internalFormat,
                                width, height, 0, format, type, initialData.IsEmpty ? null : pointer + offset);
                        }
                        offset += length;
                    }
                }
            }
            finally { Gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment); }
        }

        private void ValidatePixelData(ReadOnlySpan<byte> pixels)
        {
            if (!_description.IsCompressed && GetFormat(_description.Format).BytesPerPixel == 0)
                throw new NotSupportedException($"CPU upload for {_description.Format} textures is not supported.");
            var expectedLength = _description.DataByteSize;
            if (pixels.Length != expectedLength)
                throw new ArgumentException($"Texture '{Label}' requires {expectedLength} bytes, got {pixels.Length}.", nameof(pixels));
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
