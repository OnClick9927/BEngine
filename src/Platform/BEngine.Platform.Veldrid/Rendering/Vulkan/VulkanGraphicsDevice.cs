using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using Veldrid;
using Vd = Veldrid;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public sealed class VulkanGraphicsDevice : IGraphicsPresentationDevice, IGraphicsResourceRetirement, IGraphicsResourceDisposal, IGraphicsResourceRetirementMaintenance,
    IGraphicsDeviceStatistics, IGraphicsColorReadback, IGraphicsTextureFormatSupport,
    IGraphicsFrameTimingSource, IGraphicsPassTimingSource, IGraphicsRenderTargetScopeDevice, IGraphicsCpuFrameTimingSource
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

    private readonly Vd.GraphicsDevice _device;
    private readonly FieldInfo? _presentModeField;
    private readonly bool _attachmentDepthDependency;
    private bool _logPresentMode;
    private readonly GraphicsBackend _backend;
    private readonly ResourceFactory _factory;
    private sealed class FrameResources(CommandList commands, Fence fence, Action<uint, RgbaFloat, float, byte>? clearColorDepth)
    {
        internal readonly CommandList Commands = commands;
        internal readonly Fence Fence = fence;
        internal readonly Action<uint, RgbaFloat, float, byte>? ClearColorDepth = clearColorDepth;
        internal readonly List<VulkanColorReadbackRequest> Readbacks = [];
        internal VulkanTimestampQueries? Timestamps;
        internal long Serial;
        internal bool Submitted;
    }
    private readonly FrameResources[] _frames;
    private int _frameIndex;
    private FrameResources CurrentFrame => _frames[_frameIndex];
    private CommandList _commands => CurrentFrame.Commands;
    private Fence _frameFence => CurrentFrame.Fence;
    private readonly Queue<(long Serial, IDisposable Resource)> _retiredResources = new();
    public int framesInFlight => _frames.Length;
    internal long CompletedFrameSerial { get; private set; }
    private bool _disposingRetiredResources;
    private readonly List<VulkanColorReadbackRequest> _recordingReadbacks = [];
    private List<VulkanColorReadbackRequest> _submittedReadbacks => CurrentFrame.Readbacks;
    private readonly GraphicsDeviceCapabilities _capabilities;
    private VulkanProgram? _activeProgram;
    private readonly VulkanTexture2D?[] _boundTextures = new VulkanTexture2D?[8];
    private GraphicsDepthState _depthState = GraphicsDepthState.Default;
    private GraphicsBlendMode _blendMode;
    private GraphicsRasterizerState _rasterizerState = GraphicsRasterizerState.Default;
    private GraphicsRect _viewport;
    private GraphicsRect? _scissor;
    private GraphicsRect? _recordedViewport, _recordedScissor;
    private Pipeline? _recordedPipeline;
    private ResourceSet? _recordedResources;
    private DeviceBuffer? _recordedVertices;
    private VulkanRenderTarget? _activeTarget;
    private readonly List<TargetState> _targetScopes = new(4);
    private long _targetScopeSequence;
    private Framebuffer ActiveFramebuffer => _activeTarget?.Framebuffer ?? _device.MainSwapchain.Framebuffer;
    private int _frameWidth;
    private int _frameHeight;
    private GraphicsDrawStatistics _drawStatistics;
    private bool _frameOpen;
    private bool _frameSubmitted { get => CurrentFrame.Submitted; set => CurrentFrame.Submitted = value; }
    private bool _disposed;
    private bool _timingFrame;
    internal long FrameSerial { get; private set; }
    internal bool IsRecording => _frameOpen;
    // Kept internal for an independent old-path/new-path pixel regression.
    internal bool combinedAttachmentClearEnabled { get; set; } = true;
    private VulkanTimestampQueries? _timestamps => CurrentFrame.Timestamps;
    public bool SupportsGpuFrameTiming => _timestamps is not null;
    public IDisposable? BeginGpuPass(string name) => _timestamps?.BeginPass(name);
    public bool CollectCpuFrameTiming { get; set; }
    public GraphicsCpuFrameTiming CpuFrameTiming { get; private set; }
    // Normal frames bound retirement work between resources. Explicit synchronization
    // and device shutdown still drain every safe retirement, including budget leases.
    internal int retirementResourceBudget { get; set; } = 64;
    internal long retirementTickBudget { get; set; } = Math.Max(1, Stopwatch.Frequency / 5000);

    public GraphicsBackend Backend
    {
        get { return _backend; }
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

    [DynamicDependency("BEngineAttachmentDepthDependency", "Veldrid.Vk.VkGraphicsDevice", "Veldrid")]
    [DynamicDependency("BEngineDeferredImageAcquireVersion", "Veldrid.Vk.VkSwapchain", "Veldrid")]
    public VulkanGraphicsDevice(
        Vd.GraphicsDevice device,
        GraphicsBackend backend = GraphicsBackend.Vulkan)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        var nativeBackend = backend switch
        {
            GraphicsBackend.Vulkan => Vd.GraphicsBackend.Vulkan,
            GraphicsBackend.OpenGLES => Vd.GraphicsBackend.OpenGLES,
            GraphicsBackend.Metal => Vd.GraphicsBackend.Metal,
            _ => throw new ArgumentOutOfRangeException(nameof(backend),
                "The Veldrid presentation device supports Vulkan, OpenGL ES, or Metal.")
        };
        if (device.BackendType != nativeBackend)
            throw new ArgumentException(
                $"The native {device.BackendType} device cannot be exposed as {backend}.", nameof(backend));
        _backend = backend;
        if (_backend == GraphicsBackend.Vulkan)
        {
            _presentModeField = device.MainSwapchain.GetType().GetField("BEnginePresentMode", BindingFlags.Public | BindingFlags.Instance);
            _attachmentDepthDependency = device.GetType().GetField("BEngineAttachmentDepthDependency",
                BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is int dependencyVersion && dependencyVersion == 1;
            LogPresentMode();
        }
        _factory = device.ResourceFactory;
        _frames = new FrameResources[backend == GraphicsBackend.Vulkan ? 2 : 1];
        for (var index = 0; index < _frames.Length; index++)
        {
            var commands = _factory.CreateCommandList();
            _frames[index] = new(commands, _factory.CreateFence(false),
                backend == GraphicsBackend.Vulkan ? ResolveCombinedClear(commands) : null);
        }
        if (backend == GraphicsBackend.Vulkan &&
            Environment.GetEnvironmentVariable("BENGINE_FRAME_PROBE") == "1")
        {
            Debug.Log($"BENGINE_VULKAN_ATTACHMENTS|combinedClear={_frames.All(static frame => frame.ClearColorDepth is not null)}|privateDepth={_attachmentDepthDependency}");
            // Startup-only proof comes from the actually loaded dependency,
            // including a trimmed single-file player, rather than a loose DLL.
            var acquirePatch = device.MainSwapchain.GetType().GetField("BEngineDeferredImageAcquireVersion",
                BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
            AppContext.TryGetSwitch("BEngine.Vulkan.DeferSwapchainAcquireWait", out var deferAcquire);
            Debug.Log($"BENGINE_VULKAN_ACQUIRE|patchVersion={acquirePatch ?? 0}|optIn={deferAcquire}|scope=main-swapchain|drain=framebuffer+submit+lifecycle");
        }
        _capabilities = new GraphicsDeviceCapabilities(
            backend,
            device.DeviceName,
            $"{backend} {device.ApiVersion}",
            SupportedFeatures,
            [GraphicsShaderLanguage.Glsl, GraphicsShaderLanguage.SpirV])
        {
            DeviceVendor = device.VendorName
        };
        GraphicsBackendSettings.ReportActive(_capabilities);
        if (_backend == GraphicsBackend.Vulkan)
            foreach (var frame in _frames)
                try { frame.Timestamps = new VulkanTimestampQueries(_device, frame.Commands); } catch (NotSupportedException) { }
    }

    public void BeginFrame(int width, int height)
    {
        ThrowIfDisposed();
        if (_frameOpen) throw new InvalidOperationException("The Vulkan frame is already open.");
        // The patched getter completes a pending swapchain-image acquire before
        // any framebuffer use. Its fence can overlap the preceding CPU update;
        // frame-resource fences below still separately protect command reuse.
        var capture = CollectCpuFrameTiming;
        var timing = capture ? new GraphicsCpuFrameTiming { Frame = FrameSerial + 1, AcquireStartTicks = Stopwatch.GetTimestamp() } : default;
        var swapchain = _device.MainSwapchain;
        var framebuffer = swapchain.Framebuffer;
        if (capture) timing.AcquireEndTicks = Stopwatch.GetTimestamp();
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        CompleteFrame(CurrentFrame);
        if (capture) timing.FrameFenceEndTicks = Stopwatch.GetTimestamp();
        var retired = DisposeRetiredResources(drain: false);
        if (capture) { timing.RetireEndTicks = Stopwatch.GetTimestamp(); timing.RetiredResources = retired; }
        FrameSerial++; CurrentFrame.Serial = FrameSerial;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width != _frameWidth || height != _frameHeight)
        {
            CompleteSubmittedFrame();
            swapchain.Resize((uint)width, (uint)height);
            framebuffer = swapchain.Framebuffer;
            _frameWidth = width;
            _frameHeight = height;
        }
        if (capture) timing.ResizeEndTicks = Stopwatch.GetTimestamp();
        _commands.Begin();
        // Native state belongs to this command list recording, never the last
        // frame (frames in flight rotate between independent command lists).
        _recordedViewport = _recordedScissor = null;
        _recordedPipeline = null; _recordedResources = null; _recordedVertices = null;
        if (_timingFrame) _timestamps?.Begin(_commands, FrameSerial);
        _commands.SetFramebuffer(framebuffer);
        _frameOpen = true;
        SetViewportCore(new GraphicsRect(0, 0, width, height));
        if (capture)
        {
            timing.RecordingEndTicks = Stopwatch.GetTimestamp();
            timing.PendingRetirements = _retiredResources.Count;
            CpuFrameTiming = timing;
        }
        else CpuFrameTiming = default;
    }

    public void SetVSync(bool enabled)
    {
        ThrowIfDisposed();
        _logPresentMode |= _backend == GraphicsBackend.Vulkan && _device.SyncToVerticalBlank != enabled;
        _device.SyncToVerticalBlank = enabled;
    }

    private void LogPresentMode()
    {
        if (_presentModeField is not null)
            Debug.Log($"Vulkan present mode: {_presentModeField.GetValue(_device.MainSwapchain)}; vsync={_device.SyncToVerticalBlank}; device={_device.DeviceName}.");
        _logPresentMode = false;
    }

    public void Present()
    {
        ThrowIfDisposed();
        if (!_frameOpen) throw new InvalidOperationException("No Vulkan frame is open.");
        if (_activeTarget is not null) throw new InvalidOperationException("Dispose render target scopes before presenting.");
        var capture = CollectCpuFrameTiming;
        var timing = CpuFrameTiming;
        if (capture) timing.PresentStartTicks = Stopwatch.GetTimestamp();
        _timestamps?.End();
        _commands.End();
        try
        {
            if (capture) timing.SubmitStartTicks = Stopwatch.GetTimestamp();
            _device.SubmitCommands(_commands, _frameFence);
            if (capture) timing.SubmitEndTicks = Stopwatch.GetTimestamp();
            if (_timingFrame && _timestamps is not null) _timestamps.Submitted();
            _frameSubmitted = true;
            SubmitReadbacks();
            // Profiling must not add a CPU wait. Consume ready queries now, or after
            // the normal frame-resource fence completes before the next recording.
            _timestamps?.TryComplete();
            if (capture) timing.QueriesEndTicks = timing.SwapStartTicks = Stopwatch.GetTimestamp();
            _device.SwapBuffers();
            if (capture) timing.SwapEndTicks = Stopwatch.GetTimestamp();
            // Veldrid applies a VSync change while acquiring the next image.
            // Log after that recreation, once per change rather than per frame.
            if (_logPresentMode) LogPresentMode();
        }
        catch (Exception exception)
        {
            if (!_frameSubmitted) AbandonRecordingReadbacks(exception.Message);
            throw;
        }
        finally
        {
            if (capture) CpuFrameTiming = timing;
            _frameOpen = false;
            _timingFrame = false;
        }
    }

    public void BeginGpuFrameTiming()
    {
        ThrowIfDisposed();
        _timingFrame = SupportsGpuFrameTiming && GraphicsFrameTimingService.IsEnabled;
    }

    public void EndGpuFrameTiming()
    {
        ThrowIfDisposed();
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

    public bool SupportsTextureFormat(GraphicsTextureFormat format)
    {
        ThrowIfDisposed();
        if (format == GraphicsTextureFormat.Astc4x4Unorm) return VulkanAstcImage.Supports(_device);
        try { return _device.GetPixelFormatSupport(VulkanTexture2D.ToVeldrid(format), TextureType.Texture2D, TextureUsage.Sampled); }
        catch (NotSupportedException) { return false; }
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
        return new VulkanTexture2D(this, label, description, initialData);
    }

    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        description.Validate();
        return new VulkanRenderTarget(this, label, description);
    }

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
        => new RenderTargetScope(this, PushRenderTargetState(renderTarget));

    public long PushRenderTargetState(IGraphicsRenderTarget renderTarget)
    {
        RequireFrame();
        var target = RequireResource<VulkanRenderTarget>(renderTarget);
        target.ThrowIfDisposed();
        var token = checked(_targetScopeSequence + 1);
        _targetScopes.Add(new(token, _activeTarget, _viewport, _scissor));
        _targetScopeSequence = token;
        _activeTarget = target;
        _commands.SetFramebuffer(target.Framebuffer);
        SetViewportCore(new GraphicsRect(0, 0, target.Width, target.Height));
        SetScissor(null);
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
            return; // A copied or public scope that already completed is idempotent.
        }
        RequireFrame();
        var previous = _targetScopes[last];
        _targetScopes.RemoveAt(last);
        _activeTarget = previous.PreviousTarget;
        Commands.SetFramebuffer(ActiveFramebuffer);
        SetViewportCore(previous.Viewport);
        SetScissor(previous.Scissor);
    }

    private bool IsPreviousTarget(VulkanRenderTarget target)
    {
        foreach (var state in _targetScopes)
            if (ReferenceEquals(state.PreviousTarget, target)) return true;
        return false;
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
        if (surfaceWidth != ActiveFramebuffer.Width || surfaceHeight != ActiveFramebuffer.Height)
            return GraphicsColorReadbackRequest.Unavailable(region,
                "The requested surface size does not match the active Vulkan framebuffer.");

        var colorTargets = ActiveFramebuffer.ColorTargets;
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
        if (_recordedViewport == viewport) return;
        _recordedViewport = viewport;
        _commands.SetViewport(0, new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, 0, 1));
    }

    public void SetScissor(GraphicsRect? scissor)
    {
        RequireFrame();
        _scissor = scissor;
        var value = scissor ?? new GraphicsRect(0, 0, _viewport.Width, _viewport.Height);
        value.Validate();
        var absolute = new GraphicsRect(Math.Max(0, _viewport.X + value.X), Math.Max(0, _viewport.Y + value.Y),
            Math.Max(0, value.Width), Math.Max(0, value.Height));
        if (_recordedScissor == absolute) return;
        _recordedScissor = absolute;
        _commands.SetScissorRect(0,
            (uint)Math.Max(0, _viewport.X + value.X),
            (uint)Math.Max(0, _viewport.Y + value.Y),
            (uint)Math.Max(0, value.Width), (uint)Math.Max(0, value.Height));
    }

    public void Clear(GraphicsClearFlags flags, NVector4 color)
    {
        RequireFrame();
        using var timing = GraphicsFrameTimingService.IsPassTimingEnabled
            ? BeginGpuPass(_activeTarget is null ? "RHI.ClearAttachments.Primary" : "RHI.ClearAttachments.Offscreen") : null;
        var framebuffer = ActiveFramebuffer;
        var clearColor = (flags & GraphicsClearFlags.Color) != 0 && framebuffer.ColorTargets.Count > 0;
        var clearDepth = (flags & (GraphicsClearFlags.Depth | GraphicsClearFlags.Stencil)) != 0 &&
            framebuffer.DepthTarget is not null;
        var depth = (flags & GraphicsClearFlags.Depth) != 0 ? 1f : 0f;
        if (clearColor && clearDepth && combinedAttachmentClearEnabled && CurrentFrame.ClearColorDepth is { } combined)
        {
            // No command can observe an intermediate color-only clear inside
            // this synchronous RHI call. Commit both attachments before return,
            // including clear-only/readback frames; no pass barrier is removed.
            combined(0, new RgbaFloat(color.X, color.Y, color.Z, color.W), depth, 0);
            return;
        }
        if (clearColor)
            _commands.ClearColorTarget(0, new RgbaFloat(color.X, color.Y, color.Z, color.W));
        if (clearDepth) _commands.ClearDepthStencil(depth, 0);
    }

    [DynamicDependency("BEngineClearColorDepth", "Veldrid.Vk.VkCommandList", "Veldrid")]
    private static Action<uint, RgbaFloat, float, byte>? ResolveCombinedClear(CommandList commands)
    {
        // VeldridSafety patches build output before trimming/bundling. An
        // unpatched third-party dependency keeps the original immediate path.
        var method = commands.GetType().GetMethod("BEngineClearColorDepth", BindingFlags.Public | BindingFlags.Instance,
            null, [typeof(uint), typeof(RgbaFloat), typeof(float), typeof(byte)], null);
        return method?.ReturnType == typeof(void)
            ? method.CreateDelegate<Action<uint, RgbaFloat, float, byte>>(commands) : null;
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
        ThrowIfDisposed();
        if ((uint)slot >= (uint)_boundTextures.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        var resource = RequireResource<VulkanTexture2D>(texture);
        resource.RequireSampled();
        _boundTextures[slot] = resource;
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
        program.Prepare(vkMesh, _boundTextures);
        var vertices = vkMesh.Buffer;
        if (!ReferenceEquals(_recordedVertices, vertices))
        { _commands.SetVertexBuffer(0, vertices); _recordedVertices = vertices; }
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

    internal void UploadSnapshot<T>(DeviceBuffer buffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        // Each recorded draw owns its buffer version until the frame fence completes.
        // Dynamic buffers are host coherent in Veldrid's Vulkan backend. Host writes
        // before submission avoid its transfer-to-uniform barrier and render-pass splits.
        SynchronizeUpload();
        _device.UpdateBuffer(buffer, 0, data);
    }

    internal void SynchronizeUpload()
    {
        if (!_frameOpen) CompleteSubmittedFrame();
    }

    public void RetireResource(IDisposable resource)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(resource);
        UnbindTexture(resource);
        _retiredResources.Enqueue((FrameSerial, resource));
        // Disposing a resource is not an implicit GPU-wide synchronization point.
        // Its serial and dependent leases remain queued until a normal frame fence
        // completes, or an explicit synchronize/resize/readback/shutdown drains it.
    }

    public bool TryFlushRetiredResources()
    {
        ThrowIfDisposed();
        if (_frameOpen || _disposingRetiredResources) return false;
        CompleteSubmittedFrame();
        return true;
    }

    public void DisposeResource(IDisposable resource, IDisposable dependent)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(dependent);
        if (resource is IGraphicsResource graphicsResource && !ReferenceEquals(graphicsResource.Device, this))
            throw new ArgumentException("The graphics resource belongs to another device.", nameof(resource));
        // Native texture/framebuffer Dispose enqueues its own handles. Queue the lease
        // after those handles, rather than releasing it before a second retirement.
        try { resource.Dispose(); }
        finally { RetireResource(dependent); }
    }

    private void UnbindTexture(IDisposable resource)
    {
        for (var slot = 0; slot < _boundTextures.Length; slot++)
            if (ReferenceEquals(resource, _boundTextures[slot])) _boundTextures[slot] = null;
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
        foreach (var frame in _frames)
        {
            frame.Timestamps?.Complete(); frame.Timestamps?.Dispose(); frame.Timestamps = null;
            foreach (var request in frame.Readbacks) request.Abandon("The graphics device was disposed.");
            frame.Readbacks.Clear(); frame.Submitted = false;
        }
        CompletedFrameSerial = long.MaxValue;
        _fallbackTexture?.Dispose();
        _fallbackTexture = null;
        foreach (var frame in _frames) { frame.Commands.Dispose(); frame.Fence.Dispose(); }
        DisposeRetiredResources();
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

    private void CompleteFrame(FrameResources frame)
    {
        if (!frame.Submitted) return;
        _device.WaitForFence(frame.Fence); _device.ResetFence(frame.Fence);
        frame.Submitted = false;
        CompletedFrameSerial = Math.Max(CompletedFrameSerial, frame.Serial);
        foreach (var request in frame.Readbacks) request.MarkGpuCompleted();
        frame.Readbacks.Clear(); frame.Timestamps?.Complete();
    }
    private void CompleteSubmittedFrame()
    {
        foreach (var frame in _frames) CompleteFrame(frame);
        DisposeRetiredResources();
    }

    private void SubmitReadbacks()
    {
        if (_recordingReadbacks.Count == 0) return;
        _submittedReadbacks.AddRange(_recordingReadbacks);
        _recordingReadbacks.Clear();
    }

    private void AbandonRecordingReadbacks(string error)
    {
        foreach (var request in _recordingReadbacks) request.Abandon(error);
        _recordingReadbacks.Clear();
    }

    private bool TryCompleteReadback(VulkanColorReadbackRequest request)
    {
        if (request.GpuCompleted) return true;
        if (!_frameOpen)
        {
            // Avoid LINQ's capturing predicate on the readback polling path.
            // This path is commonly called once per editor repaint while a
            // capture is pending, so the direct bounded scan keeps polling
            // allocation-free and preserves the same fence semantics.
            for (var index = 0; index < _frames.Length; index++)
                if (_frames[index].Submitted && _frames[index].Readbacks.Contains(request))
                {
                    CompleteSubmittedFrame();
                    break;
                }
        }
        return request.GpuCompleted;
    }

    private int DisposeRetiredResources(bool drain = true)
    {
        if (_disposingRetiredResources) return 0;
        _disposingRetiredResources = true;
        var count = 0;
        var limit = drain ? int.MaxValue : Math.Max(1, retirementResourceBudget);
        var started = !drain && _retiredResources.Count != 0 ? Stopwatch.GetTimestamp() : 0;
        try
        {
            while (_retiredResources.TryPeek(out var entry) && entry.Serial <= CompletedFrameSerial)
            {
                if (count >= limit || (!drain && count > 0 && Stopwatch.GetTimestamp() - started >= retirementTickBudget)) break;
                _retiredResources.Dequeue(); count++; entry.Resource.Dispose();
            }
        }
        finally { _disposingRetiredResources = false; }
        return count;
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
            var staging = _staging;
            _staging = null;
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
        private readonly VulkanUploadBuffer _uploads;
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
        public DeviceBuffer Buffer
        {
            get { _uploads.MarkReferenced(_buffer); return _buffer; }
        }

        public VulkanMesh(VulkanGraphicsDevice device, GraphicsMeshDescription description)
            : base(device, description.Label)
        {
            _layout = description.Layout;
            _topology = description.Topology;
            _usage = description.Usage;
            _uploads = new VulkanUploadBuffer(device, BufferUsage.VertexBuffer);
            _buffer = _uploads.Upload(description.Vertices.Span);
            _vertexCount = description.Vertices.Length * sizeof(float) / _layout.StrideBytes;
        }

        public void Update(ReadOnlySpan<float> vertices)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var bytes = vertices.Length * sizeof(float);
            if (bytes % _layout.StrideBytes != 0)
                throw new ArgumentException("Vertex data length must be a multiple of the layout stride.", nameof(vertices));
            _buffer = _uploads.Upload(vertices);
            _vertexCount = bytes / _layout.StrideBytes;
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _uploads.Dispose();
        }
    }

    private sealed class VulkanTexture2D : VulkanResource, IGraphicsTexture2D
    {
        private BEngine.Diagnostics.NativeMemoryTracker.Allocation? _memory;
        private VulkanAstcImage? _astc;
        private bool _disposed;
        private long _sampledFrame = -1;
        private readonly TextureUsage _usage;
        private readonly GraphicsTextureDescription _description;
        private TextureView? _view;
        private readonly Sampler? _sampler;
        internal event Action<VulkanTexture2D>? ResourcesInvalidated;
        public GraphicsTextureDescription Description
        {
            get { return _description; }
        }
        public Vd.Texture Texture { get; private set; } = null!;
        public TextureView View => _view ?? throw new InvalidOperationException("An attachment-only texture has no sampled view.");
        public Sampler Sampler => _sampler ?? throw new InvalidOperationException("An attachment-only texture has no sampler.");

        public VulkanTexture2D(
            VulkanGraphicsDevice device,
            string label,
            GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData,
            TextureUsage usage = TextureUsage.Sampled) : base(device, label)
        {
            _description = description;
            _usage = usage;
            var astc = description.Format == GraphicsTextureFormat.Astc4x4Unorm;
            try
            {
            Texture = device.Factory.CreateTexture(TextureDescription.Texture2D(
                astc ? 1u : (uint)description.Width, astc ? 1u : (uint)description.Height, astc ? 1u : (uint)description.MipLevels, 1,
                astc ? PixelFormat.R8_G8_B8_A8_UNorm : ToVeldrid(description.Format), usage));
            if ((usage & TextureUsage.Sampled) != 0)
            {
                _view = device.Factory.CreateTextureView(Texture);
                var addressMode = ToVeldrid(description.AddressMode);
                _sampler = device.Factory.CreateSampler(new SamplerDescription(
                    addressMode,
                    addressMode,
                    addressMode,
                    (description.Trilinear ? SamplerFilter.MinLinear_MagLinear_MipLinear : ToVeldrid(description.MinFilter, description.MagFilter)),
                    null, 0, 0, (uint)(description.MipLevels - 1), 0, SamplerBorderColor.TransparentBlack));
            }
            if (astc) { device.SynchronizeUpload(); _astc = new VulkanAstcImage(device, description, initialData); }
            else { if (!initialData.IsEmpty) Update(initialData); _memory = BEngine.Diagnostics.NativeMemoryTracker.Register("GPU.Texture." + device.Backend, label, description.DataByteSize); }
            }
            catch
            {
                _astc?.Dispose(); _sampler?.Dispose(); _view?.Dispose(); Texture?.Dispose(); _memory?.Dispose();
                throw;
            }
        }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((_usage & TextureUsage.Sampled) == 0)
                throw new InvalidOperationException("An attachment-only texture cannot be updated with sampled pixel data.");
            var required = _description.DataByteSize;
            if (pixels.Length != required) throw new ArgumentException("Texture pixel data is incomplete.", nameof(pixels));
            VulkanDevice.SynchronizeUpload();
            if (_astc is not null)
            {
                var replacement = new VulkanAstcImage(VulkanDevice, _description, pixels);
                ResourcesInvalidated?.Invoke(this); VulkanDevice.RetireResource(_astc); _astc = replacement; _sampledFrame = -1; return;
            }
            // Waiting for the GPU cannot protect draws that are recorded but not submitted.
            // Preserve their image and descriptors; subsequent draws use a new image version.
            if (_sampledFrame > VulkanDevice.CompletedFrameSerial ||
                VulkanDevice.IsRecording && _sampledFrame == VulkanDevice.FrameSerial)
            {
                if (_usage != TextureUsage.Sampled)
                    throw new InvalidOperationException("A render target cannot be updated while it is used by the current frame.");
                var replacement = VulkanDevice.Factory.CreateTexture(TextureDescription.Texture2D(
                    (uint)_description.Width, (uint)_description.Height, (uint)_description.MipLevels, 1, ToVeldrid(_description.Format), _usage));
                TextureView replacementView;
                try { replacementView = VulkanDevice.Factory.CreateTextureView(replacement); }
                catch { replacement.Dispose(); throw; }
                ResourcesInvalidated?.Invoke(this);
                VulkanDevice.RetireResource(View);
                VulkanDevice.RetireResource(new TrackedNativeResource(Texture, _memory));
                _memory = BEngine.Diagnostics.NativeMemoryTracker.Register("GPU.Texture." + VulkanDevice.Backend, Label, _description.DataByteSize);
                Texture = replacement;
                _view = replacementView;
            }
            var offset = 0;
            for (var level = 0; level < _description.MipLevels; level++)
            {
                var length = _description.LevelByteSize(level);
                VulkanDevice.NativeDevice.UpdateTexture(Texture, pixels.Slice(offset, length), 0, 0, 0,
                    (uint)Math.Max(1, _description.Width >> level), (uint)Math.Max(1, _description.Height >> level), 1, (uint)level, 0);
                offset += length;
            }
            _sampledFrame = -1;
        }

        public void MarkSampled() => _sampledFrame = VulkanDevice.FrameSerial;
        public void RequireSampled()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((_usage & TextureUsage.Sampled) == 0)
                throw new InvalidOperationException("An attachment-only texture cannot be bound for sampling.");
        }
        public void BindNativeImage(ResourceSet resources, uint binding) => _astc?.Bind(resources, binding);

        public override void Dispose()
        {
            if (_disposed) return;
            ResourcesInvalidated?.Invoke(this);
            ResourcesInvalidated = null;
            VulkanDevice.UnbindTexture(this);
            _disposed = true;
            if (_astc is not null) { VulkanDevice.RetireResource(_astc); _astc = null; }
            if (_sampler is not null) VulkanDevice.RetireResource(_sampler);
            if (_view is not null) { VulkanDevice.RetireResource(_view); _view = null; }
            VulkanDevice.RetireResource(new TrackedNativeResource(Texture, _memory)); _memory = null;
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

        internal static PixelFormat ToVeldrid(GraphicsTextureFormat format) => format switch
        {
            GraphicsTextureFormat.Bc1RgbaUnorm => PixelFormat.BC1_Rgba_UNorm,
            GraphicsTextureFormat.Bc3RgbaUnorm => PixelFormat.BC3_UNorm,
            GraphicsTextureFormat.Bc7RgbaUnorm => PixelFormat.BC7_UNorm,
            GraphicsTextureFormat.Etc2Rgba8Unorm => PixelFormat.ETC2_R8_G8_B8_A8_UNorm,
            GraphicsTextureFormat.R8Unorm => PixelFormat.R8_UNorm,
            GraphicsTextureFormat.Rgba8Unorm => PixelFormat.R8_G8_B8_A8_UNorm,
            GraphicsTextureFormat.Depth24Unorm => PixelFormat.R32_Float,
            GraphicsTextureFormat.Depth24Stencil8 => PixelFormat.D24_UNorm_S8_UInt,
            _ => throw new NotSupportedException($"Texture format {format} is not supported for sampled Vulkan textures.")
        };
    }

    private sealed class TrackedNativeResource(IDisposable resource, BEngine.Diagnostics.NativeMemoryTracker.Allocation? memory) : IDisposable
    {
        public void Dispose() { try { resource.Dispose(); } finally { memory?.Dispose(); } }
    }

    private sealed class VulkanProgram : VulkanResource, IGraphicsProgram
    {
        private readonly Vd.Shader[] _shaders;
        private DeviceBuffer? _uniformBuffer;
        private readonly VulkanUploadBuffer _uniformUploads;
        private readonly ResourceLayout _uniformLayout;
        private readonly bool _textured;
        private readonly int _textureCount;
        private readonly Dictionary<PipelineKey, Pipeline> _pipelines = [];
        private readonly Dictionary<(TextureBindings Textures, DeviceBuffer Uniforms), ResourceSet> _resourceSets = [];
        // Uniform uploads rotate through frame-safe buffer versions. Keep the
        // descriptor templates bounded as well: an unbounded map would retain a
        // ResourceSet for every changed uniform snapshot until program disposal.
        // Entries are retired through the device so an eviction never releases a
        // descriptor still referenced by an in-flight command list.
        private readonly Queue<((TextureBindings Textures, DeviceBuffer Uniforms) Key, ResourceSet Value)> _resourceSetOrder = [];
        private const int MaxResourceSetTemplates = 256;
        private readonly HashSet<VulkanTexture2D> _subscribedTextures = [];
        private readonly float[] _uniforms;
        private readonly Dictionary<string, GraphicsUniformBinding> _uniformBindings;
        private readonly Dictionary<string, int> _vectorArrayCapacities = new(StringComparer.Ordinal);
        private bool _uniformDirty = true;
        private bool _disposed;
        private GraphicsVertexLayout? _preparedLayout;
        private GraphicsPrimitiveTopology _preparedTopology;
        private GraphicsBlendMode _preparedBlend;
        private GraphicsDepthState _preparedDepth;
        private GraphicsRasterizerState _preparedRasterizer;
        private Framebuffer? _preparedFramebuffer;
        private Pipeline? _preparedPipeline;
        private TextureBindings _preparedTextures;
        private DeviceBuffer? _preparedUniforms;
        private ResourceSet? _preparedResourceSet;

        public VulkanProgram(VulkanGraphicsDevice device, GraphicsShaderProgramDescription description)
            : base(device, description.Label)
        {
            _uniforms = new float[description.UniformBufferSizeBytes / sizeof(float)];
            _uniformBindings = description.Uniforms.ToDictionary(binding => binding.Name, StringComparer.Ordinal);
            foreach (var binding in description.Uniforms)
                if (binding.Name.EndsWith("[0]", StringComparison.Ordinal))
                {
                    var prefix = binding.Name[..^3]; var count = 0;
                    while (_uniformBindings.TryGetValue(prefix + "[" + count + "]", out var element) &&
                           element.ComponentCount == 4 && element.OffsetBytes == binding.OffsetBytes + count * 16) count++;
                    _vectorArrayCapacities.Add(binding.Name, count);
                }
            _textureCount = description.SampledTextureCount;
            _textured = _textureCount > 1 || description.Label.Contains("Texture", StringComparison.OrdinalIgnoreCase);
            var vertex = new ShaderDescription(ShaderStages.Vertex,
                SpirvShaderCache.Compile(description.VertexShader), description.VertexShader.EntryPoint);
            var fragment = new ShaderDescription(ShaderStages.Fragment,
                SpirvShaderCache.Compile(description.FragmentShader), description.FragmentShader.EntryPoint);
            var firstShader = device.Factory.CreateShader(vertex);
            try { _shaders = [firstShader, device.Factory.CreateShader(fragment)]; }
            catch { firstShader.Dispose(); throw; }
            try
            {
            _uniformUploads = new VulkanUploadBuffer(device, BufferUsage.UniformBuffer);
            var elements = new ResourceLayoutElementDescription[1 + _textureCount * 2];
            elements[0] = new("UIViewport", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment);
            for (var slot = 0; slot < _textureCount; slot++)
            {
                elements[1 + slot * 2] = new("Texture" + slot, ResourceKind.TextureReadOnly, ShaderStages.Fragment);
                elements[2 + slot * 2] = new("Sampler" + slot, ResourceKind.Sampler, ShaderStages.Fragment);
            }
            _uniformLayout = device.Factory.CreateResourceLayout(new ResourceLayoutDescription(elements));
            }
            catch { foreach (var shader in _shaders) shader.Dispose(); throw; }
        }

        public void Bind()
        {
            VulkanDevice.Bind(this);
        }
        public void SetMatrix4x4(string name, NMatrix4x4 value) => SetUniform(name,
            [value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24,
             value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44]);
        public void SetVector4(string name, NVector4 value) => SetUniform(name, [value.X, value.Y, value.Z, value.W]);
        public void SetVector4Array(string firstElementName, ReadOnlySpan<NVector4> values)
        {
            if (!firstElementName.EndsWith("[0]", StringComparison.Ordinal)) throw new ArgumentException("Expected an array [0] uniform.", nameof(firstElementName));
            if (!_uniformBindings.TryGetValue(firstElementName, out var binding)) return;
            if (values.Length > _vectorArrayCapacities.GetValueOrDefault(firstElementName))
                throw new ArgumentException("The values exceed the contiguous vec4 array binding.", nameof(values));
            var source = System.Runtime.InteropServices.MemoryMarshal.Cast<NVector4, float>(values);
            var destination = _uniforms.AsSpan(binding.OffsetBytes / sizeof(float), source.Length);
            if (source.SequenceEqual(destination)) return;
            source.CopyTo(destination); _uniformDirty = true;
        }
        public void SetFloat(string name, float value)
        {
            SetUniform(name, [value]);
        }
        public void SetInt(string name, int value) => SetUniform(name, [BitConverter.Int32BitsToSingle(value)]);

        private void SetUniform(string name, ReadOnlySpan<float> values)
        {
            if (!_uniformBindings.TryGetValue(name, out var binding)) return;
            if (binding.ComponentCount != values.Length)
                throw new ArgumentException("The value does not match the shader uniform binding.", nameof(name));
            var destination = _uniforms.AsSpan(binding.OffsetBytes / sizeof(float), values.Length);
            if (values.SequenceEqual(destination)) return;
            values.CopyTo(destination);
            _uniformDirty = true;
        }

        public void Prepare(VulkanMesh mesh, IReadOnlyList<VulkanTexture2D?> boundTextures)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var textures = new TextureBindings(Slot(0), Slot(1), Slot(2), Slot(3), Slot(4), Slot(5), Slot(6), Slot(7));
            VulkanTexture2D? Slot(int slot)
            {
                if (slot >= _textureCount) return null;
                var texture = boundTextures[slot];
                if (_textured && texture is null) throw new InvalidOperationException($"The Vulkan program requires texture slot {slot}.");
                texture ??= VulkanDevice.CreateFallbackTexture();
                texture.MarkSampled();
                return texture;
            }
            if (_uniformDirty)
            {
                _uniformBuffer = _uniformUploads.Upload<float>(_uniforms);
                _uniformDirty = false;
            }
            _uniformUploads.MarkReferenced(_uniformBuffer!);
            var framebuffer = VulkanDevice.ActiveFramebuffer;
            var blend = VulkanDevice.BlendMode;
            var depth = VulkanDevice.DepthState;
            var rasterizer = VulkanDevice.RasterizerState;
            var pipeline = _preparedPipeline;
            // Sprite, particle and UI batches commonly keep the same layout and
            // render state. Avoid hashing the composite output description for
            // every draw; the framebuffer identity also changes on resize.
            if (pipeline is null || !ReferenceEquals(_preparedLayout, mesh.LayoutUnchecked) ||
                _preparedTopology != mesh.TopologyUnchecked || _preparedBlend != blend ||
                _preparedDepth != depth || _preparedRasterizer != rasterizer ||
                !ReferenceEquals(_preparedFramebuffer, framebuffer))
            {
                var key = new PipelineKey(mesh.LayoutUnchecked, mesh.TopologyUnchecked, blend,
                    depth, rasterizer, framebuffer.OutputDescription);
                if (!_pipelines.TryGetValue(key, out pipeline))
                {
                    pipeline = CreatePipeline(mesh, key);
                    _pipelines.Add(key, pipeline);
                }
                _preparedLayout = mesh.LayoutUnchecked; _preparedTopology = mesh.TopologyUnchecked;
                _preparedBlend = blend; _preparedDepth = depth; _preparedRasterizer = rasterizer;
                _preparedFramebuffer = framebuffer; _preparedPipeline = pipeline;
            }
            if (!ReferenceEquals(VulkanDevice._recordedPipeline, pipeline))
            {
                VulkanDevice.Commands.SetPipeline(pipeline);
                VulkanDevice._recordedPipeline = pipeline;
                // Pipeline-layout changes invalidate the descriptor binding.
                VulkanDevice._recordedResources = null;
            }
            var resourceSet = _preparedResourceSet;
            if (resourceSet is null || !ReferenceEquals(_preparedUniforms, _uniformBuffer) ||
                _preparedTextures != textures)
            {
                var resourceKey = (textures, _uniformBuffer!);
                if (!_resourceSets.TryGetValue(resourceKey, out resourceSet))
                {
                    var resources = new BindableResource[1 + _textureCount * 2];
                    resources[0] = _uniformBuffer!;
                    for (var slot = 0; slot < _textureCount; slot++)
                    {
                        var texture = textures[slot]!;
                        resources[1 + slot * 2] = texture.View;
                        resources[2 + slot * 2] = texture.Sampler;
                        if (_subscribedTextures.Add(texture)) texture.ResourcesInvalidated += OnTextureInvalidated;
                    }
                    resourceSet = VulkanDevice.Factory.CreateResourceSet(new ResourceSetDescription(_uniformLayout, resources));
                    for (var slot = 0; slot < _textureCount; slot++) textures[slot]!.BindNativeImage(resourceSet, (uint)(1 + slot * 2));
                    _resourceSets.Add(resourceKey, resourceSet);
                    _resourceSetOrder.Enqueue((resourceKey, resourceSet));
                    TrimResourceSetTemplates();
                }
                _preparedTextures = textures; _preparedUniforms = _uniformBuffer;
                _preparedResourceSet = resourceSet;
            }
            if (!ReferenceEquals(VulkanDevice._recordedResources, resourceSet))
            {
                VulkanDevice.Commands.SetGraphicsResourceSet(0, resourceSet);
                VulkanDevice._recordedResources = resourceSet;
            }
        }

        private void OnTextureInvalidated(VulkanTexture2D texture)
        {
            if (_preparedTextures.Contains(texture))
            { _preparedResourceSet = null; _preparedTextures = default; _preparedUniforms = null; }
            texture.ResourcesInvalidated -= OnTextureInvalidated;
            _subscribedTextures.Remove(texture);
            foreach (var key in _resourceSets.Keys.Where(key => key.Textures.Contains(texture)).ToArray())
                if (_resourceSets.Remove(key, out var resourceSet)) VulkanDevice.RetireResource(resourceSet);
        }

        private void TrimResourceSetTemplates()
        {
            while (_resourceSets.Count > MaxResourceSetTemplates && _resourceSetOrder.TryDequeue(out var candidate))
            {
                // The queue can contain stale entries after texture invalidation
                // or a previous eviction. Match the value as well as the key so a
                // later re-created template is never removed by an old queue item.
                if (!_resourceSets.TryGetValue(candidate.Key, out var current) ||
                    !ReferenceEquals(current, candidate.Value)) continue;
                _resourceSets.Remove(candidate.Key);
                if (ReferenceEquals(_preparedResourceSet, candidate.Value))
                {
                    _preparedResourceSet = null;
                    _preparedTextures = default;
                    _preparedUniforms = null;
                }
                VulkanDevice.RetireResource(candidate.Value);
            }
        }

        private readonly record struct TextureBindings(VulkanTexture2D? T0, VulkanTexture2D? T1, VulkanTexture2D? T2,
            VulkanTexture2D? T3, VulkanTexture2D? T4, VulkanTexture2D? T5, VulkanTexture2D? T6, VulkanTexture2D? T7)
        {
            internal VulkanTexture2D? this[int slot] => slot switch
            { 0 => T0, 1 => T1, 2 => T2, 3 => T3, 4 => T4, 5 => T5, 6 => T6, 7 => T7, _ => null };
            internal bool Contains(VulkanTexture2D texture)
            {
                for (var slot = 0; slot < 8; slot++) if (ReferenceEquals(this[slot], texture)) return true;
                return false;
            }
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
                ? new BlendStateDescription(RgbaFloat.Clear, new BlendAttachmentDescription(true,
                    BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha, BlendFunction.Add,
                    BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add))
                : BlendStateDescription.SingleOverrideBlend;
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
                depthClipEnabled: true,
                scissorTestEnabled: true);
            var description = new GraphicsPipelineDescription(
                blend,
                depth,
                rasterizer,
                mesh.TopologyUnchecked == GraphicsPrimitiveTopology.LineList
                    ? PrimitiveTopology.LineList : PrimitiveTopology.TriangleList,
                shaderSet,
                [_uniformLayout],
                key.Output);
            return VulkanDevice.Factory.CreateGraphicsPipeline(description);
        }

        public override void Dispose()
        {
            if (_disposed) return;
            foreach (var texture in _subscribedTextures) texture.ResourcesInvalidated -= OnTextureInvalidated;
            _subscribedTextures.Clear();
            foreach (var resourceSet in _resourceSets.Values) VulkanDevice.RetireResource(resourceSet);
            _resourceSets.Clear();
            _resourceSetOrder.Clear();
            _preparedResourceSet = null; _preparedTextures = default; _preparedUniforms = null;
            foreach (var pipeline in _pipelines.Values) VulkanDevice.RetireResource(pipeline);
            _pipelines.Clear();
            _preparedLayout = null; _preparedFramebuffer = null; _preparedPipeline = null;
            VulkanDevice.RetireResource(_uniformLayout);
            _uniformUploads.Dispose();
            foreach (var shader in _shaders) VulkanDevice.RetireResource(shader);
            _disposed = true;
        }

        private readonly record struct PipelineKey(
            GraphicsVertexLayout Layout,
            GraphicsPrimitiveTopology Topology,
            GraphicsBlendMode BlendMode,
            GraphicsDepthState DepthState,
            GraphicsRasterizerState RasterizerState,
            OutputDescription Output);
    }

    private VulkanTexture2D? _fallbackTexture;

    private sealed class VulkanRenderTarget : VulkanResource, IGraphicsRenderTarget
    {
        private readonly VulkanTexture2D? _color;
        private readonly VulkanTexture2D? _depth;
        private readonly GraphicsRenderTargetDescription _description;
        private bool _disposed;

        internal VulkanRenderTarget(VulkanGraphicsDevice device, string label,
            GraphicsRenderTargetDescription description) : base(device, label)
        {
            _description = description;
            try
            {
                if (description.ColorFormat is { } colorFormat)
                    _color = new VulkanTexture2D(device, label + ".Color",
                        new GraphicsTextureDescription(description.Width, description.Height, colorFormat,
                            GraphicsTextureUsage.Sampled, description.Filter, description.Filter), [],
                        TextureUsage.Sampled | TextureUsage.RenderTarget);
                if (description.DepthFormat is { } depthFormat)
                {
                    // SampleDepth=false never exposes this private attachment.
                    // Keep it in the depth layout, provided the output dependency
                    // was patched to make depth writes visible to later LOADs.
                    // Older/third-party Veldrid and other backends retain their
                    // original sampled-depth path until independently verified.
                    var sampledDepth = description.SampleDepth || !device._attachmentDepthDependency;
                    _depth = new VulkanTexture2D(device, label + ".Depth",
                        new GraphicsTextureDescription(description.Width, description.Height, depthFormat,
                            sampledDepth ? GraphicsTextureUsage.Sampled : GraphicsTextureUsage.RenderTarget,
                            GraphicsTextureFilter.Nearest, GraphicsTextureFilter.Nearest), [],
                        TextureUsage.DepthStencil | (sampledDepth ? TextureUsage.Sampled : 0));
                }
                Framebuffer = device.Factory.CreateFramebuffer(new FramebufferDescription(
                    _depth?.Texture, _color is null ? [] : [_color.Texture]));
            }
            catch
            {
                _depth?.Dispose();
                _color?.Dispose();
                throw;
            }
        }

        internal Framebuffer Framebuffer { get; }
        public int Width => _description.Width;
        public int Height => _description.Height;
        public IGraphicsTexture2D? ColorTexture => _description.SampleColor ? _color : null;
        public IGraphicsTexture2D? DepthTexture => _description.SampleDepth ? _depth : null;
        internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        public override void Dispose()
        {
            if (_disposed) return;
            if (ReferenceEquals(VulkanDevice._activeTarget, this) ||
                VulkanDevice.IsPreviousTarget(this))
                throw new InvalidOperationException("A bound render target cannot be disposed.");
            _disposed = true;
            VulkanDevice.RetireResource(Framebuffer);
            _depth?.Dispose();
            _color?.Dispose();
        }
    }

    private readonly record struct TargetState(long Token, VulkanRenderTarget? PreviousTarget, GraphicsRect Viewport, GraphicsRect? Scissor);

    private sealed class RenderTargetScope(VulkanGraphicsDevice owner, long token) : IDisposable
    {
        private VulkanGraphicsDevice? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } owner) return;
            owner.PopRenderTargetState(token);
            _owner = null;
        }
    }

    private VulkanTexture2D CreateFallbackTexture() => _fallbackTexture ??= new VulkanTexture2D(
        this,
        "BEngine.Vulkan.White",
        new GraphicsTextureDescription(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled),
        [255, 255, 255, 255]);
}
