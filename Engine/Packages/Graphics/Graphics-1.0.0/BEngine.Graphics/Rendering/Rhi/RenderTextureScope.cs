namespace BEngine.Rendering.Rhi;

/// <summary>Binds a Graphics-owned texture or a transient RHI target and restores the caller binding.</summary>
/// <remarks>
/// Scopes belong to one render thread and must be released in reverse order. Completed copies
/// are harmless. Asset targets remain alive until their last binding ends, even if released
/// or resized meanwhile. A warmed token-capable device needs no per-scope allocation.
/// Direct native target changes inside a scope must be restored before it is disposed.
/// </remarks>
public readonly struct RenderTextureScope : IDisposable
{
    [ThreadStatic] private static BindingStack? _current;
    private readonly BindingStack? _owner;
    private readonly int _depth;
    private readonly long _token;

    /// <summary>Dimensions of the target requested by this scope; zero when inheriting the caller target.</summary>
    public int Width { get; }
    public int Height { get; }

    private RenderTextureScope(BindingStack owner, int depth, long token, IGraphicsRenderTarget? target)
    { _owner = owner; _depth = depth; _token = token; Width = target?.Width ?? 0; Height = target?.Height ?? 0; }

    internal static RenderTexture? Active => _current?.Active;

    /// <summary>Binds a texture. Null inherits the caller target, including its identity on the same device.</summary>
    public static RenderTextureScope Push(IGraphicsDevice device, RenderTexture? texture)
    {
        ArgumentNullException.ThrowIfNull(device);
        var owner = PrepareStack();
        var resource = texture?.AcquireTarget(device);
        return PushCore(owner, device, resource?.Target, texture, resource);
    }

    /// <summary>Binds an intermediate RHI target with no RenderTexture identity.</summary>
    public static RenderTextureScope PushTransient(IGraphicsDevice device, IGraphicsRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);
        return PushCore(PrepareStack(), device, target, null, null);
    }

    private static BindingStack PrepareStack()
    {
        var owner = _current ??= new BindingStack();
        owner.Frames.EnsureCapacity(owner.Frames.Count + 1);
        _ = checked(owner.Sequence + 1);
        return owner;
    }

    private static RenderTextureScope PushCore(BindingStack owner, IGraphicsDevice device,
        IGraphicsRenderTarget? target, RenderTexture? texture, RenderTexture.TargetResource? resource)
    {
        GraphicsRenderTargetScope native;
        try { native = GraphicsRenderTargetScope.Push(device, target); }
        catch { resource?.ReleaseBinding(); throw; }
        var depth = owner.Frames.Count;
        if (target is null && depth > 0 && ReferenceEquals(owner.Frames[depth - 1].Device, device))
        {
            texture = owner.Frames[depth - 1].Texture;
            resource = owner.Frames[depth - 1].Resource;
            resource?.Acquire();
        }
        var token = ++owner.Sequence;
        owner.Frames.Add(new(token, device, texture, resource, native));
        return new(owner, depth, token, target);
    }

    public void Dispose()
    {
        if (_owner is null) return;
        if (!ReferenceEquals(_current, _owner))
            throw new InvalidOperationException("A render texture scope must be released on its owning render thread.");
        var frames = _owner.Frames;
        if (frames.Count <= _depth || frames[_depth].Token != _token) return;
        if (frames.Count != _depth + 1)
            throw new InvalidOperationException("Render texture scopes must be released in reverse order.");
        var frame = frames[_depth];
        // Retain logical state and pins if the native scope fails to restore, so disposal can be retried.
        frame.Native.Dispose();
        frames.RemoveAt(_depth);
        frame.Resource?.ReleaseBinding();
    }

    private readonly record struct Frame(long Token, IGraphicsDevice Device, RenderTexture? Texture,
        RenderTexture.TargetResource? Resource, GraphicsRenderTargetScope Native);

    private sealed class BindingStack
    {
        internal readonly List<Frame> Frames = [];
        internal long Sequence;
        internal RenderTexture? Active
        {
            get
            {
                if (Frames.Count == 0) return null;
                var frame = Frames[^1];
                return frame.Texture is { isDestroyed: false } texture && texture.IsCreated() &&
                    frame.Resource is not { IsRetired: true } ? texture : null;
            }
        }
    }
}
