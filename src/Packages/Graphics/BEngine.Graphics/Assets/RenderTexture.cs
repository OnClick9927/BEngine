using BEngine.Rendering.Rhi;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
[CreateAssetMenu(fileName = "New Render Texture", menuName = "Rendering/Render Texture", order = 210)]
public sealed class RenderTexture : Texture, IDisposable
{
    private TargetResource? _resource;
    private bool _createRequested = true;

    /// <summary>The live RenderTexture bound by the current render-thread scope.</summary>
    public static RenderTexture? active => RenderTextureScope.Active;

    public RenderTextureFormat format { get; set; } = RenderTextureFormat.Rgba8;
    public bool useDepth { get; set; } = true;
    public bool autoGenerateMips { get; set; }

    public RenderTexture() : this(256, 256) { }

    public RenderTexture(int width, int height, RenderTextureFormat format = RenderTextureFormat.Rgba8)
        : base(width, height, linear: true)
    {
        this.format = format;
        name = "Render Texture";
    }

    public bool IsCreated() => _resource is not null;

    public bool Create()
    {
        ThrowIfDestroyed();
        _createRequested = true;
        return true;
    }

    public void Resize(int width, int height)
    {
        ThrowIfDestroyed();
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (this.width == width && this.height == height) return;
        this.width = width;
        this.height = height;
        RetireTarget();
        _createRequested = true;
    }

    public void Release()
    {
        RetireTarget();
        _createRequested = false;
    }

    public void Dispose()
    {
        DestroyImmediate(this);
        GC.SuppressFinalize(this);
    }

    protected override void ReleaseAssetResources()
    {
        Release();
        base.ReleaseAssetResources();
    }

    internal TargetResource AcquireTarget(IGraphicsDevice device)
    {
        ThrowIfDestroyed();
        ArgumentNullException.ThrowIfNull(device);
        if (!_createRequested) throw new InvalidOperationException("RenderTexture.Create must be called first.");
        var description = new GraphicsRenderTargetDescription(width, height,
                format == RenderTextureFormat.R8
                    ? GraphicsTextureFormat.R8Unorm
                    : GraphicsTextureFormat.Rgba8Unorm,
                useDepth ? GraphicsTextureFormat.Depth24Stencil8 : null,
                SampleColor: true,
                Filter: filterMode == TextureFilterMode.Point
                    ? GraphicsTextureFilter.Nearest
                    : GraphicsTextureFilter.Linear);
        if (_resource is null || !ReferenceEquals(_resource.Device, device) ||
            _resource.Description != description)
        {
            // Create before replacing: a failed allocation keeps the earlier target usable.
            var replacement = new TargetResource(device,
                device.CreateRenderTarget($"BEngine.RenderTexture.{GetInstanceID()}", description), description);
            var previous = _resource;
            _resource = replacement;
            previous?.Retire();
        }
        _resource.Acquire();
        return _resource;
    }

    internal bool TryGetSampleTexture(IGraphicsDevice device, out IGraphicsTexture2D texture)
    {
        texture = null!;
        if (_resource is null || !ReferenceEquals(_resource.Device, device) || _resource.Target.ColorTexture is null) return false;
        texture = _resource.Target.ColorTexture;
        return true;
    }

    private void RetireTarget()
    {
        var previous = _resource;
        _resource = null;
        previous?.Retire();
    }

    // A binding pins this resource, even after Release, Resize, a device switch,
    // or a descriptor change detaches it from the asset. No per-binding allocation.
    internal sealed class TargetResource(IGraphicsDevice device, IGraphicsRenderTarget target,
        GraphicsRenderTargetDescription description)
    {
        private int _bindings;
        internal IGraphicsDevice Device => device;
        internal IGraphicsRenderTarget Target => target;
        internal GraphicsRenderTargetDescription Description => description;
        internal bool IsRetired { get; private set; }
        internal void Acquire() => _bindings++;
        internal void ReleaseBinding()
        {
            _bindings--;
            if (_bindings == 0 && IsRetired) target.Dispose();
        }
        internal void Retire()
        {
            if (IsRetired) return;
            IsRetired = true;
            if (_bindings == 0) target.Dispose();
        }
    }
}
