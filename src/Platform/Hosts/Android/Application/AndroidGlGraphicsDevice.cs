using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Android.Opengl;
using BEngine.Rendering.Rhi;
using Java.Nio;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Player.Hosts.Android;

/// <summary>OpenGL ES 3 presentation device backed by Android's platform GLES bindings.</summary>
internal sealed class AndroidGlGraphicsDevice : IGraphicsPresentationDevice, IGraphicsDeviceStatistics,
    IGraphicsTextureFormatSupport, IGraphicsColorReadback
{
    private const int GlVendor = 0x1F00;
    private const int GlRenderer = 0x1F01;
    private const int GlVersion = 0x1F02;
    private const int GlExtensions = 0x1F03;
    private const int GlMaxTextureSize = 0x0D33;
    private const int GlVertexShader = 0x8B31;
    private const int GlFragmentShader = 0x8B30;
    private const int GlCompileStatus = 0x8B81;
    private const int GlLinkStatus = 0x8B82;
    private const int GlArrayBuffer = 0x8892;
    private const int GlStaticDraw = 0x88E4;
    private const int GlDynamicDraw = 0x88E8;
    private const int GlFloat = 0x1406;
    private const int GlTriangles = 0x0004;
    private const int GlLines = 0x0001;
    private const int GlTexture2D = 0x0DE1;
    private const int GlTexture0 = 0x84C0;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlTextureMaxLevel = 0x813D;
    private const int GlNearest = 0x2600;
    private const int GlLinear = 0x2601;
    private const int GlNearestMipmapNearest = 0x2700;
    private const int GlLinearMipmapNearest = 0x2701;
    private const int GlLinearMipmapLinear = 0x2703;
    private const int GlClampToEdge = 0x812F;
    private const int GlRepeat = 0x2901;
    private const int GlMirroredRepeat = 0x8370;
    private const int GlUnpackAlignment = 0x0CF5;
    private const int GlPackAlignment = 0x0D05;
    private const int GlR8 = 0x8229;
    private const int GlRed = 0x1903;
    private const int GlRgba8 = 0x8058;
    private const int GlRgba = 0x1908;
    private const int GlUnsignedByte = 0x1401;
    private const int GlDepthComponent24 = 0x81A6;
    private const int GlDepthComponent = 0x1902;
    private const int GlUnsignedInt = 0x1405;
    private const int GlDepth24Stencil8 = 0x88F0;
    private const int GlDepthStencil = 0x84F9;
    private const int GlUnsignedInt248 = 0x84FA;
    private const int GlEtc2Rgba8 = 0x9278;
    private const int GlAstc4X4Rgba = 0x93B0;
    private const int GlFramebuffer = 0x8D40;
    private const int GlFramebufferBinding = 0x8CA6;
    private const int GlColorAttachment0 = 0x8CE0;
    private const int GlDepthAttachment = 0x8D00;
    private const int GlDepthStencilAttachment = 0x821A;
    private const int GlFramebufferComplete = 0x8CD5;
    private const int GlNone = 0;
    private const int GlScissorTest = 0x0C11;
    private const int GlColorBufferBit = 0x00004000;
    private const int GlDepthBufferBit = 0x00000100;
    private const int GlStencilBufferBit = 0x00000400;
    private const int GlDepthTest = 0x0B71;
    private const int GlBlend = 0x0BE2;
    private const int GlSrcAlpha = 0x0302;
    private const int GlOneMinusSrcAlpha = 0x0303;
    private const int GlCullFace = 0x0B44;
    private const int GlBack = 0x0405;
    private const int GlFront = 0x0404;
    private const int GlCcw = 0x0901;
    private const int GlPolygonOffsetFill = 0x8037;

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

    private readonly bool _supportsAstc;
    private GraphicsDrawStatistics _drawStatistics;
    private GraphicsRect _viewport;
    private int _emptyVertexArray;
    private int _currentProgram;
    private GraphicsDepthState? _depthState;
    private GraphicsBlendMode? _blendMode;
    private GraphicsRasterizerState? _rasterizerState;
    private GraphicsRect? _scissorRect;
    private bool _scissorKnown;
    private bool _disposed;

    public AndroidGlGraphicsDevice()
    {
        var extensions = GLES20.GlGetString(GlExtensions) ?? string.Empty;
        _supportsAstc = extensions.Contains("GL_KHR_texture_compression_astc_ldr", StringComparison.Ordinal);
        var maximum = new int[1];
        GLES20.GlGetIntegerv(GlMaxTextureSize, maximum, 0);
        Capabilities = new GraphicsDeviceCapabilities(
            GraphicsBackend.OpenGLES,
            GLES20.GlGetString(GlRenderer) ?? "Android OpenGL ES",
            GLES20.GlGetString(GlVersion) ?? "OpenGL ES 3",
            Features,
            [GraphicsShaderLanguage.Glsl])
        {
            DeviceVendor = GLES20.GlGetString(GlVendor) ?? "Unknown",
            MaxTextureSize = maximum[0]
        };
        GraphicsUtility.Current.ReportActive(Capabilities);
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGLES;
    public GraphicsDeviceCapabilities Capabilities { get; }
    public GraphicsDrawStatistics DrawStatistics => _drawStatistics;
    public GraphicsColorReadbackCapabilities ColorReadbackCapabilities =>
        new(true, GraphicsColorReadbackValidation.DefaultMaximumBytes);

    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        description.Validate();
        if (description.VertexShader.Language != GraphicsShaderLanguage.Glsl ||
            description.FragmentShader.Language != GraphicsShaderLanguage.Glsl)
            throw new NotSupportedException("Android OpenGL ES accepts GLSL shader source.");
        return new AndroidGlProgram(this, description.Label,
            NormalizeShader(description.VertexShader.Code), NormalizeShader(description.FragmentShader.Code));
    }

    public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        return new AndroidGlMesh(this, description);
    }

    public bool SupportsTextureFormat(GraphicsTextureFormat format)
    {
        ThrowIfDisposed();
        return format is GraphicsTextureFormat.R8Unorm or GraphicsTextureFormat.Rgba8Unorm or
            GraphicsTextureFormat.Depth24Unorm or GraphicsTextureFormat.Depth24Stencil8 or
            GraphicsTextureFormat.Etc2Rgba8Unorm ||
            format == GraphicsTextureFormat.Astc4x4Unorm && _supportsAstc;
    }

    public IGraphicsTexture2D CreateTexture2D(
        string label, GraphicsTextureDescription description, ReadOnlySpan<byte> initialData = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        if (!SupportsTextureFormat(description.Format))
            throw new NotSupportedException($"Android OpenGL ES does not support {description.Format}.");
        return new AndroidGlTexture(this, label, description, initialData, ownsHandle: true);
    }

    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        return new AndroidGlRenderTarget(this, label, description);
    }

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
    {
        var target = Require<AndroidGlRenderTarget>(renderTarget);
        var previous = GetInteger(GlFramebufferBinding);
        NativeBindFramebuffer(GlFramebuffer, target.Handle);
        return new FramebufferScope(this, previous);
    }

    public void SetViewport(GraphicsRect viewport)
    {
        ThrowIfDisposed();
        viewport.Validate();
        if (_viewport == viewport) return;
        _viewport = viewport;
        _scissorKnown = false;
        NativeViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        ThrowIfDisposed();
        scissor?.Validate();
        if (_scissorKnown && _scissorRect == scissor) return;
        _scissorKnown = true; _scissorRect = scissor;
        if (scissor is null)
        {
            NativeDisable(GlScissorTest);
            return;
        }
        NativeEnable(GlScissorTest);
        NativeScissor(
            _viewport.X + scissor.Value.X,
            _viewport.Y + _viewport.Height - scissor.Value.Y - scissor.Value.Height,
            scissor.Value.Width,
            scissor.Value.Height);
    }

    public void Clear(GraphicsClearFlags flags, NumericsVector4 color)
    {
        ThrowIfDisposed();
        var mask = 0;
        if ((flags & GraphicsClearFlags.Color) != 0)
        {
            NativeClearColor(color.X, color.Y, color.Z, color.W);
            mask |= GlColorBufferBit;
        }
        if ((flags & GraphicsClearFlags.Depth) != 0) mask |= GlDepthBufferBit;
        if ((flags & GraphicsClearFlags.Stencil) != 0) mask |= GlStencilBufferBit;
        if (mask != 0) NativeClear(mask);
    }

    public void SetDepthState(GraphicsDepthState state)
    {
        ThrowIfDisposed();
        if (_depthState == state) return;
        _depthState = state;
        if (state.TestEnabled) NativeEnable(GlDepthTest);
        else NativeDisable(GlDepthTest);
        GLES20.GlDepthMask(state.WriteEnabled);
    }

    public void SetBlendMode(GraphicsBlendMode mode)
    {
        ThrowIfDisposed();
        if (_blendMode == mode) return;
        switch (mode)
        {
            case GraphicsBlendMode.Disabled:
                NativeDisable(GlBlend);
                break;
            case GraphicsBlendMode.AlphaBlend:
                NativeEnable(GlBlend);
                NativeBlendFuncSeparate(GlSrcAlpha, GlOneMinusSrcAlpha, 1, GlOneMinusSrcAlpha);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
        _blendMode = mode;
    }

    public void SetRasterizerState(GraphicsRasterizerState state)
    {
        ThrowIfDisposed();
        if (_rasterizerState == state) return;
        _rasterizerState = state;
        if (state.CullMode == GraphicsCullMode.None) NativeDisable(GlCullFace);
        else
        {
            NativeEnable(GlCullFace);
            NativeCullFace(state.CullMode == GraphicsCullMode.Back ? GlBack : GlFront);
            NativeFrontFace(GlCcw);
        }
        if (state.DepthBiasEnabled)
        {
            NativeEnable(GlPolygonOffsetFill);
            NativePolygonOffset(state.SlopeScale, state.ConstantBias);
        }
        else NativeDisable(GlPolygonOffsetFill);
    }

    public void BindTexture(int slot, IGraphicsTexture2D texture)
    {
        ThrowIfDisposed();
        if (slot is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(slot));
        var typed = Require<AndroidGlTexture>(texture);
        if ((typed.Description.Usage & GraphicsTextureUsage.Sampled) == 0)
            throw new InvalidOperationException($"Texture '{texture.Label}' was not created for sampling.");
        NativeActiveTexture(GlTexture0 + slot);
        NativeBindTexture(GlTexture2D, typed.Handle);
    }

    public void Draw(IGraphicsMesh mesh)
    {
        var typed = Require<AndroidGlMesh>(mesh);
        Draw(typed, typed.VertexCount, 0);
    }

    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
    {
        var typed = Require<AndroidGlMesh>(mesh);
        if (vertexCount < 0 || firstVertex < 0 || firstVertex + vertexCount > typed.VertexCount)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        NativeBindVertexArray(typed.VertexArray);
        NativeDrawArrays(ToGl(typed.Topology), firstVertex, vertexCount);
        RecordDraw(vertexCount, typed.Topology);
    }

    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
    {
        ThrowIfDisposed();
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstVertex < 0) throw new ArgumentOutOfRangeException(nameof(firstVertex));
        if (_emptyVertexArray == 0) _emptyVertexArray = GenerateVertexArray();
        NativeBindVertexArray(_emptyVertexArray);
        NativeDrawArrays(ToGl(topology), firstVertex, vertexCount);
        RecordDraw(vertexCount, topology);
    }

    public void BeginFrame(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        // The GLSurfaceView host disables scissoring before its surface clear.
        _scissorKnown = false;
        NativeBindFramebuffer(GlFramebuffer, 0);
    }

    public void Present()
    {
        ThrowIfDisposed();
        NativeFlush();
    }

    public void SetVSync(bool enabled)
    {
        ThrowIfDisposed();
        // GLSurfaceView owns the EGL swap interval and presents on the display cadence.
    }

    public GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region, int surfaceWidth, int surfaceHeight)
    {
        ThrowIfDisposed();
        try
        {
            GraphicsColorReadbackValidation.ValidateRegion(region, surfaceWidth, surfaceHeight,
                GraphicsColorReadbackValidation.DefaultMaximumBytes);
            var pixels = new byte[checked(region.Width * region.Height * 4)];
            var previous = GetInteger(GlPackAlignment);
            try
            {
                GLES20.GlPixelStorei(GlPackAlignment, 1);
                using var buffer = CreateBuffer(pixels.Length);
                GLES20.GlReadPixels(region.X, surfaceHeight - region.Y - region.Height,
                    region.Width, region.Height, GlRgba, GlUnsignedByte, buffer);
                buffer.Position(0);
                buffer.Get(pixels);
                FlipRows(pixels, region.Width, region.Height);
                return GraphicsColorReadbackRequest.Ready(region,
                    GraphicsColorReadbackImage.FromOwnedRgba8(region.Width, region.Height, pixels));
            }
            finally { GLES20.GlPixelStorei(GlPackAlignment, previous); }
        }
        catch (Exception exception)
        {
            return GraphicsColorReadbackRequest.Failed(region, exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_emptyVertexArray != 0)
            GLES30.GlDeleteVertexArrays(1, [_emptyVertexArray], 0);
        _emptyVertexArray = 0;
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

    private T Require<T>(IGraphicsResource resource) where T : AndroidGlResource
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is not T typed || !ReferenceEquals(typed.Device, this))
            throw new ArgumentException("The graphics resource belongs to another Android OpenGL ES device.",
                nameof(resource));
        typed.ThrowIfDisposed();
        return typed;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void UseProgram(int program)
    {
        if (_currentProgram == program) return;
        NativeUseProgram(program);
        _currentProgram = program;
    }

    private static string NormalizeShader(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var normalized = source.Replace("#version 330 core", "#version 300 es\nprecision highp float;",
            StringComparison.Ordinal);
        return normalized.Contains("#version 300 es", StringComparison.Ordinal)
            ? normalized
            : "#version 300 es\nprecision highp float;\n" + normalized;
    }

    private static int GetInteger(int name)
    {
        var values = new int[1];
        GLES20.GlGetIntegerv(name, values, 0);
        return values[0];
    }

    private static int GenerateVertexArray()
    {
        var handles = new int[1];
        GLES30.GlGenVertexArrays(1, handles, 0);
        if (handles[0] == 0) throw new InvalidOperationException("Unable to allocate an OpenGL ES vertex array.");
        return handles[0];
    }

    private static int GenerateBuffer()
    {
        var handles = new int[1];
        GLES20.GlGenBuffers(1, handles, 0);
        if (handles[0] == 0) throw new InvalidOperationException("Unable to allocate an OpenGL ES buffer.");
        return handles[0];
    }

    private static int GenerateTexture()
    {
        var handles = new int[1];
        GLES20.GlGenTextures(1, handles, 0);
        if (handles[0] == 0) throw new InvalidOperationException("Unable to allocate an OpenGL ES texture.");
        return handles[0];
    }

    private static int GenerateFramebuffer()
    {
        var handles = new int[1];
        GLES20.GlGenFramebuffers(1, handles, 0);
        if (handles[0] == 0) throw new InvalidOperationException("Unable to allocate an OpenGL ES framebuffer.");
        return handles[0];
    }

    private static ByteBuffer CreateBuffer(ReadOnlySpan<byte> data)
    {
        var buffer = CreateBuffer(data.Length);
        if (!data.IsEmpty) buffer.Put(data.ToArray());
        buffer.Position(0);
        return buffer;
    }

    private static ByteBuffer CreateBuffer(int byteCount)
    {
        var buffer = ByteBuffer.AllocateDirect(Math.Max(byteCount, 1)) ??
            throw new InvalidOperationException("Unable to allocate a native OpenGL ES transfer buffer.");
        buffer.Order(ByteOrder.NativeOrder()!);
        buffer.Position(0);
        return buffer;
    }

    private static int ToGl(GraphicsPrimitiveTopology topology) => topology switch
    {
        GraphicsPrimitiveTopology.TriangleList => GlTriangles,
        GraphicsPrimitiveTopology.LineList => GlLines,
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

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

    private abstract class AndroidGlResource(AndroidGlGraphicsDevice device, string label) : IGraphicsResource
    {
        private bool _disposed;
        public IGraphicsDevice Device => device;
        protected AndroidGlGraphicsDevice Owner => device;
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

    private sealed class AndroidGlProgram : AndroidGlResource, IGraphicsProgram
    {
        private readonly Dictionary<string, int> _uniforms = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NumericsMatrix4x4> _matrices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NumericsVector4> _vectors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _floats = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _integers = new(StringComparer.Ordinal);
        private int _handle;
        private readonly float[] _matrix = new float[16];

        public AndroidGlProgram(AndroidGlGraphicsDevice device, string label, string vertex, string fragment)
            : base(device, label)
        {
            var vertexHandle = CompileShader(GlVertexShader, label + " vertex", vertex);
            var fragmentHandle = CompileShader(GlFragmentShader, label + " fragment", fragment);
            try
            {
                _handle = GLES20.GlCreateProgram();
                if (_handle == 0) throw new InvalidOperationException($"Unable to allocate program '{label}'.");
                GLES20.GlAttachShader(_handle, vertexHandle);
                GLES20.GlAttachShader(_handle, fragmentHandle);
                GLES20.GlLinkProgram(_handle);
                var status = new int[1];
                GLES20.GlGetProgramiv(_handle, GlLinkStatus, status, 0);
                if (status[0] == 0)
                    throw new InvalidOperationException(
                        $"OpenGL ES program '{label}' failed to link: {GLES20.GlGetProgramInfoLog(_handle)}");
            }
            catch
            {
                if (_handle != 0) GLES20.GlDeleteProgram(_handle);
                _handle = 0;
                throw;
            }
            finally
            {
                GLES20.GlDeleteShader(vertexHandle);
                GLES20.GlDeleteShader(fragmentHandle);
            }
        }

        public void Bind()
        {
            ThrowIfDisposed();
            Owner.UseProgram(_handle);
        }

        public unsafe void SetMatrix4x4(string name, NumericsMatrix4x4 value)
        {
            ThrowIfDisposed();
            Bind();
            if (_matrices.TryGetValue(name, out var previous) && previous == value) return;
            _matrices[name] = value;
            _matrix[0]=value.M11; _matrix[1]=value.M12; _matrix[2]=value.M13; _matrix[3]=value.M14;
            _matrix[4]=value.M21; _matrix[5]=value.M22; _matrix[6]=value.M23; _matrix[7]=value.M24;
            _matrix[8]=value.M31; _matrix[9]=value.M32; _matrix[10]=value.M33; _matrix[11]=value.M34;
            _matrix[12]=value.M41; _matrix[13]=value.M42; _matrix[14]=value.M43; _matrix[15]=value.M44;
            fixed (float* matrix = _matrix)
                NativeUniformMatrix4fv(GetUniform(name), 1, 0, (nint)matrix);
        }

        public void SetVector4(string name, NumericsVector4 value)
        {
            ThrowIfDisposed();
            Bind();
            if (_vectors.TryGetValue(name, out var previous) && previous == value) return;
            _vectors[name] = value;
            NativeUniform4f(GetUniform(name), value.X, value.Y, value.Z, value.W);
        }

        public void SetFloat(string name, float value)
        {
            ThrowIfDisposed();
            Bind();
            if (_floats.TryGetValue(name, out var previous) && previous.Equals(value)) return;
            _floats[name] = value;
            NativeUniform1f(GetUniform(name), value);
        }

        public void SetInt(string name, int value)
        {
            ThrowIfDisposed();
            Bind();
            if (_integers.TryGetValue(name, out var previous) && previous == value) return;
            _integers[name] = value;
            NativeUniform1i(GetUniform(name), value);
        }

        protected override void Release()
        {
            if (Owner._currentProgram == _handle) Owner._currentProgram = 0;
            if (_handle != 0) GLES20.GlDeleteProgram(_handle);
            _handle = 0;
            _uniforms.Clear();
        }

        private int GetUniform(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!_uniforms.TryGetValue(name, out var location))
            {
                location = GLES20.GlGetUniformLocation(_handle, name);
                _uniforms.Add(name, location);
            }
            return location;
        }

        private static int CompileShader(int type, string label, string source)
        {
            var handle = GLES20.GlCreateShader(type);
            if (handle == 0) throw new InvalidOperationException($"Unable to allocate shader '{label}'.");
            try
            {
                GLES20.GlShaderSource(handle, source);
                GLES20.GlCompileShader(handle);
                var status = new int[1];
                GLES20.GlGetShaderiv(handle, GlCompileStatus, status, 0);
                if (status[0] == 0)
                    throw new InvalidOperationException(
                        $"OpenGL ES shader '{label}' failed to compile: {GLES20.GlGetShaderInfoLog(handle)}");
                return handle;
            }
            catch
            {
                GLES20.GlDeleteShader(handle);
                throw;
            }
        }
    }

    private sealed class AndroidGlMesh : AndroidGlResource, IGraphicsMesh
    {
        private int _buffer;
        public int VertexArray { get; private set; }
        public GraphicsVertexLayout Layout { get; }
        public GraphicsPrimitiveTopology Topology { get; }
        public GraphicsBufferUsage Usage { get; }
        public int VertexCount { get; private set; }

        public AndroidGlMesh(AndroidGlGraphicsDevice device, GraphicsMeshDescription description)
            : base(device, description.Label)
        {
            Layout = description.Layout;
            Topology = description.Topology;
            Usage = description.Usage;
            VertexArray = GenerateVertexArray();
            _buffer = GenerateBuffer();
            try
            {
                NativeBindVertexArray(VertexArray);
                Upload(description.Vertices.Span);
                foreach (var attribute in Layout.Attributes)
                {
                    GLES20.GlEnableVertexAttribArray(attribute.Location);
                    GLES20.GlVertexAttribPointer(attribute.Location, attribute.ComponentCount, GlFloat, false,
                        Layout.StrideBytes, attribute.OffsetBytes);
                }
                NativeBindVertexArray(0);
                VertexCount = description.VertexCount;
            }
            catch
            {
                Release();
                throw;
            }
        }

        public void Update(ReadOnlySpan<float> vertices)
        {
            ThrowIfDisposed();
            if (Usage != GraphicsBufferUsage.Dynamic)
                throw new InvalidOperationException($"Mesh '{Label}' is static.");
            if (vertices.Length * sizeof(float) % Layout.StrideBytes != 0)
                throw new ArgumentException("Vertex data length must be a multiple of the layout stride.",
                    nameof(vertices));
            Upload(vertices);
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        }

        protected override void Release()
        {
            if (_buffer != 0) GLES20.GlDeleteBuffers(1, [_buffer], 0);
            if (VertexArray != 0) GLES30.GlDeleteVertexArrays(1, [VertexArray], 0);
            _buffer = 0;
            VertexArray = 0;
            VertexCount = 0;
        }

        private unsafe void Upload(ReadOnlySpan<float> vertices)
        {
            NativeBindBuffer(GlArrayBuffer, _buffer);
            // Pin only for the synchronous GL copy; no managed arrays or Java
            // direct buffers are allocated for dynamic sprite/UI/particle uploads.
            fixed (float* data = vertices)
                NativeBufferData(GlArrayBuffer, checked(vertices.Length * sizeof(float)), (nint)data,
                    Usage == GraphicsBufferUsage.Dynamic ? GlDynamicDraw : GlStaticDraw);
        }
    }

    [DllImport("libGLESv2.so", EntryPoint = "glViewport")]
    private static extern void NativeViewport(int x, int y, int width, int height);

    [DllImport("libGLESv2.so", EntryPoint = "glEnable")]
    private static extern void NativeEnable(int capability);

    [DllImport("libGLESv2.so", EntryPoint = "glDisable")]
    private static extern void NativeDisable(int capability);

    [DllImport("libGLESv2.so", EntryPoint = "glScissor")]
    private static extern void NativeScissor(int x, int y, int width, int height);

    [DllImport("libGLESv2.so", EntryPoint = "glClearColor")]
    private static extern void NativeClearColor(float r, float g, float b, float a);

    [DllImport("libGLESv2.so", EntryPoint = "glClear")]
    private static extern void NativeClear(int mask);

    [DllImport("libGLESv2.so", EntryPoint = "glBlendFuncSeparate")]
    private static extern void NativeBlendFuncSeparate(int srcRgb, int dstRgb, int srcAlpha, int dstAlpha);

    [DllImport("libGLESv2.so", EntryPoint = "glCullFace")]
    private static extern void NativeCullFace(int mode);

    [DllImport("libGLESv2.so", EntryPoint = "glFrontFace")]
    private static extern void NativeFrontFace(int mode);

    [DllImport("libGLESv2.so", EntryPoint = "glPolygonOffset")]
    private static extern void NativePolygonOffset(float factor, float units);

    [DllImport("libGLESv2.so", EntryPoint = "glActiveTexture")]
    private static extern void NativeActiveTexture(int unit);

    [DllImport("libGLESv2.so", EntryPoint = "glBindTexture")]
    private static extern void NativeBindTexture(int target, int handle);

    [DllImport("libGLESv2.so", EntryPoint = "glBindVertexArray")]
    private static extern void NativeBindVertexArray(int handle);

    [DllImport("libGLESv2.so", EntryPoint = "glDrawArrays")]
    private static extern void NativeDrawArrays(int mode, int first, int count);

    [DllImport("libGLESv2.so", EntryPoint = "glFlush")]
    private static extern void NativeFlush();

    [DllImport("libGLESv2.so", EntryPoint = "glUseProgram")]
    private static extern void NativeUseProgram(int handle);

    [DllImport("libGLESv2.so", EntryPoint = "glUniform4f")]
    private static extern void NativeUniform4f(int location, float x, float y, float z, float w);

    [DllImport("libGLESv2.so", EntryPoint = "glUniform1f")]
    private static extern void NativeUniform1f(int location, float value);

    [DllImport("libGLESv2.so", EntryPoint = "glUniform1i")]
    private static extern void NativeUniform1i(int location, int value);

    [DllImport("libGLESv2.so", EntryPoint = "glBindBuffer")]
    private static extern void NativeBindBuffer(int target, int handle);

    [DllImport("libGLESv2.so", EntryPoint = "glBindFramebuffer")]
    private static extern void NativeBindFramebuffer(int target, int handle);

    [DllImport("libGLESv2.so", EntryPoint = "glBufferData")]
    private static extern void NativeBufferData(int target, nint size, nint data, int usage);

    [DllImport("libGLESv2.so", EntryPoint = "glUniformMatrix4fv")]
    private static extern void NativeUniformMatrix4fv(int location, int count, byte transpose, nint values);

    [DllImport("libGLESv2.so", EntryPoint = "glTexImage2D")]
    private static extern void NativeTextureImage(int target, int level, int internalFormat, int width,
        int height, int border, int format, int type, nint data);

    [DllImport("libGLESv2.so", EntryPoint = "glCompressedTexImage2D")]
    private static extern void NativeCompressedTextureImage(int target, int level, int internalFormat,
        int width, int height, int border, int size, nint data);

    private sealed class AndroidGlTexture : AndroidGlResource, IGraphicsTexture2D
    {
        private readonly bool _ownsHandle;
        public int Handle { get; private set; }
        public GraphicsTextureDescription Description { get; }

        public AndroidGlTexture(AndroidGlGraphicsDevice device, string label,
            GraphicsTextureDescription description, ReadOnlySpan<byte> initialData, bool ownsHandle)
            : base(device, label)
        {
            Description = description;
            _ownsHandle = ownsHandle;
            Handle = GenerateTexture();
            try { Allocate(initialData); }
            catch { DeleteHandle(); throw; }
        }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed();
            Allocate(pixels);
        }

        protected override void Release()
        {
            if (_ownsHandle) DeleteHandle();
            else Handle = 0;
        }

        private unsafe void Allocate(ReadOnlySpan<byte> pixels)
        {
            if (!pixels.IsEmpty && pixels.Length != Description.DataByteSize)
                throw new ArgumentException(
                    $"Texture '{Label}' requires {Description.DataByteSize} bytes, got {pixels.Length}.",
                    nameof(pixels));
            if (Description.IsCompressed && pixels.IsEmpty)
                throw new ArgumentException("Compressed textures require initial data.", nameof(pixels));

            var previousTexture = GetInteger(0x8069);
            var previousAlignment = GetInteger(GlUnpackAlignment);
            NativeBindTexture(GlTexture2D, Handle);
            GLES20.GlPixelStorei(GlUnpackAlignment, 1);
            try
            {
                GLES20.GlTexParameteri(GlTexture2D, GlTextureMinFilter,
                    Description.MipLevels == 1 ? ToGl(Description.MinFilter) :
                    Description.Trilinear ? GlLinearMipmapLinear :
                    Description.MinFilter == GraphicsTextureFilter.Nearest ? GlNearestMipmapNearest : GlLinearMipmapNearest);
                GLES20.GlTexParameteri(GlTexture2D, GlTextureMaxLevel, Description.MipLevels - 1);
                GLES20.GlTexParameteri(GlTexture2D, GlTextureMagFilter, ToGl(Description.MagFilter));
                GLES20.GlTexParameteri(GlTexture2D, GlTextureWrapS, ToGl(Description.AddressMode));
                GLES20.GlTexParameteri(GlTexture2D, GlTextureWrapT, ToGl(Description.AddressMode));

                var offset = 0;
                for (var level = 0; level < Description.MipLevels; level++)
                {
                    var width = Math.Max(1, Description.Width >> level);
                    var height = Math.Max(1, Description.Height >> level);
                    var length = Description.LevelByteSize(level);
                    var levelPixels = pixels.IsEmpty ? ReadOnlySpan<byte>.Empty : pixels.Slice(offset, length);
                    fixed (byte* data = levelPixels)
                    {
                        if (Description.IsCompressed)
                            NativeCompressedTextureImage(GlTexture2D, level,
                                Description.Format == GraphicsTextureFormat.Etc2Rgba8Unorm
                                    ? GlEtc2Rgba8 : GlAstc4X4Rgba,
                                width, height, 0, length, (nint)data);
                        else
                        {
                            var format = GetFormat(Description.Format);
                            NativeTextureImage(GlTexture2D, level, format.Internal, width, height, 0,
                                format.Format, format.Type, (nint)data);
                        }
                    }
                    offset += length;
                }
            }
            finally
            {
                GLES20.GlPixelStorei(GlUnpackAlignment, previousAlignment);
                NativeBindTexture(GlTexture2D, previousTexture);
            }
        }

        private void DeleteHandle()
        {
            if (Handle != 0) GLES20.GlDeleteTextures(1, [Handle], 0);
            Handle = 0;
        }

        private static (int Internal, int Format, int Type) GetFormat(GraphicsTextureFormat format) =>
            format switch
            {
                GraphicsTextureFormat.R8Unorm => (GlR8, GlRed, GlUnsignedByte),
                GraphicsTextureFormat.Rgba8Unorm => (GlRgba8, GlRgba, GlUnsignedByte),
                GraphicsTextureFormat.Depth24Unorm =>
                    (GlDepthComponent24, GlDepthComponent, GlUnsignedInt),
                GraphicsTextureFormat.Depth24Stencil8 =>
                    (GlDepth24Stencil8, GlDepthStencil, GlUnsignedInt248),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

        private static int ToGl(GraphicsTextureFilter filter) => filter switch
        {
            GraphicsTextureFilter.Nearest => GlNearest,
            GraphicsTextureFilter.Linear => GlLinear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        private static int ToGl(GraphicsTextureAddressMode mode) => mode switch
        {
            GraphicsTextureAddressMode.ClampToEdge => GlClampToEdge,
            GraphicsTextureAddressMode.Repeat => GlRepeat,
            GraphicsTextureAddressMode.MirroredRepeat => GlMirroredRepeat,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private sealed class AndroidGlRenderTarget : AndroidGlResource, IGraphicsRenderTarget
    {
        private AndroidGlTexture? _color;
        private AndroidGlTexture? _depth;
        public int Handle { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public IGraphicsTexture2D? ColorTexture => _color;
        public IGraphicsTexture2D? DepthTexture => _depth;

        public AndroidGlRenderTarget(AndroidGlGraphicsDevice device, string label,
            GraphicsRenderTargetDescription description) : base(device, label)
        {
            Width = description.Width;
            Height = description.Height;
            var previous = GetInteger(GlFramebufferBinding);
            Handle = GenerateFramebuffer();
            try
            {
                NativeBindFramebuffer(GlFramebuffer, Handle);
                if (description.ColorFormat is { } colorFormat)
                {
                    _color = CreateAttachment(device, label + ".Color", Width, Height, colorFormat,
                        description.SampleColor, description.Filter);
                    GLES20.GlFramebufferTexture2D(GlFramebuffer, GlColorAttachment0,
                        GlTexture2D, _color.Handle, 0);
                }
                else GLES30.GlDrawBuffers(1, [GlNone], 0);

                if (description.DepthFormat is { } depthFormat)
                {
                    _depth = CreateAttachment(device, label + ".Depth", Width, Height, depthFormat,
                        description.SampleDepth, description.Filter);
                    GLES20.GlFramebufferTexture2D(GlFramebuffer,
                        depthFormat == GraphicsTextureFormat.Depth24Stencil8
                            ? GlDepthStencilAttachment : GlDepthAttachment,
                        GlTexture2D, _depth.Handle, 0);
                }

                var status = GLES20.GlCheckFramebufferStatus(GlFramebuffer);
                if (status != GlFramebufferComplete)
                    throw new InvalidOperationException(
                        $"OpenGL ES render target '{label}' is incomplete: 0x{status:X}.");
            }
            catch
            {
                Release();
                throw;
            }
            finally { NativeBindFramebuffer(GlFramebuffer, previous); }
        }

        protected override void Release()
        {
            _depth?.Dispose();
            _color?.Dispose();
            _depth = null;
            _color = null;
            if (Handle != 0) GLES20.GlDeleteFramebuffers(1, [Handle], 0);
            Handle = 0;
        }

        private static AndroidGlTexture CreateAttachment(AndroidGlGraphicsDevice device, string label,
            int width, int height, GraphicsTextureFormat format, bool sampled, GraphicsTextureFilter filter) =>
            new(device, label,
                new GraphicsTextureDescription(
                    width, height, format,
                    GraphicsTextureUsage.RenderTarget |
                    (sampled ? GraphicsTextureUsage.Sampled : GraphicsTextureUsage.None),
                    filter, filter), default, ownsHandle: true);
    }

    private sealed class FramebufferScope(AndroidGlGraphicsDevice device, int previous) : IDisposable
    {
        private AndroidGlGraphicsDevice? _device = device;
        public void Dispose()
        {
            if (_device is null) return;
            _device.ThrowIfDisposed();
            NativeBindFramebuffer(GlFramebuffer, previous);
            _device = null;
        }
    }
}
