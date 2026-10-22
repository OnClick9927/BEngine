using BEngine.Rendering.Rhi;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
[CreateAssetMenu(fileName = "New Render Texture", menuName = "Rendering/Render Texture", order = 210)]
public sealed class RenderTexture : Texture, IDisposable
{
    private IGraphicsRenderTarget? _target;
    private IGraphicsDevice? _device;
    private bool _createRequested = true;

    public static RenderTexture? active { get; internal set; }

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

    public bool IsCreated() => _target is not null;

    public bool Create()
    {
        ThrowIfDestroyed();
        _createRequested = true;
        return true;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (this.width == width && this.height == height) return;
        this.width = width;
        this.height = height;
        _target?.Dispose();
        _target = null;
        _device = null;
        _createRequested = true;
    }

    public void Release()
    {
        _target?.Dispose();
        _target = null;
        _device = null;
        _createRequested = false;
        if (ReferenceEquals(active, this)) active = null;
    }

    public void Dispose()
    {
        DestroyAsset();
        GC.SuppressFinalize(this);
    }

    protected override void ReleaseAssetResources()
    {
        Release();
        base.ReleaseAssetResources();
    }

    internal IGraphicsRenderTarget GetOrCreateTarget(IGraphicsDevice device)
    {
        ThrowIfDestroyed();
        ArgumentNullException.ThrowIfNull(device);
        if (!_createRequested) throw new InvalidOperationException("RenderTexture.Create must be called first.");
        if (_target is not null && ReferenceEquals(_device, device) &&
            _target.Width == width && _target.Height == height) return _target;
        _target?.Dispose();
        _device = device;
        _target = device.CreateRenderTarget($"BEngine.RenderTexture.{GetInstanceID()}",
            new GraphicsRenderTargetDescription(width, height,
                format == RenderTextureFormat.R8
                    ? GraphicsTextureFormat.R8Unorm
                    : GraphicsTextureFormat.Rgba8Unorm,
                useDepth ? GraphicsTextureFormat.Depth24Stencil8 : null,
                SampleColor: true,
                Filter: filterMode == TextureFilterMode.Point
                    ? GraphicsTextureFilter.Nearest
                    : GraphicsTextureFilter.Linear));
        return _target;
    }

    internal bool TryGetSampleTexture(IGraphicsDevice device, out IGraphicsTexture2D texture)
    {
        texture = null!;
        if (_target is null || !ReferenceEquals(_device, device) || _target.ColorTexture is null) return false;
        texture = _target.ColorTexture;
        return true;
    }
}
