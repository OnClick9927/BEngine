namespace BEngine.Rendering.Rhi;

/// <summary>Optional allocation-free render-target scopes for built-in renderers.</summary>
/// <remarks>Tokens are device-local. Implementations retain LIFO validation and make a successfully released token idempotent.</remarks>
public interface IGraphicsRenderTargetScopeDevice
{
    long PushRenderTargetState(IGraphicsRenderTarget renderTarget);
    void PopRenderTargetState(long token);
}

/// <summary>A non-boxed scope; unsupported devices retain their original IDisposable implementation.</summary>
public readonly struct GraphicsRenderTargetScope : IDisposable
{
    private readonly IGraphicsRenderTargetScopeDevice? _device;
    private readonly IDisposable? _fallback;
    private readonly long _token;

    private GraphicsRenderTargetScope(IGraphicsRenderTargetScopeDevice device, long token)
    { _device = device; _token = token; }
    private GraphicsRenderTargetScope(IDisposable fallback) => _fallback = fallback;

    public static GraphicsRenderTargetScope Push(IGraphicsDevice device, IGraphicsRenderTarget? renderTarget)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (renderTarget is null) return default;
        return device is IGraphicsRenderTargetScopeDevice scoped
            ? new(scoped, scoped.PushRenderTargetState(renderTarget))
            : new(device.PushRenderTarget(renderTarget));
    }

    public void Dispose()
    {
        if (_device is { } device) device.PopRenderTargetState(_token);
        else _fallback?.Dispose();
    }
}
