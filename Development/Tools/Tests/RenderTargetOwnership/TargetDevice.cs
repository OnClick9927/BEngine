using BEngine.Rendering.Rhi;
using Matrix = System.Numerics.Matrix4x4;
using Vec4 = System.Numerics.Vector4;

class TargetDevice : IGraphicsDevice
{
    readonly RecordingDevice _inner = new();
    readonly List<(long Token, TrackedTarget? Previous)> _stack = new();
    long _sequence;
    public readonly List<TrackedTarget> Targets = new();
    public TrackedTarget? Current;
    public bool FailCreate { get; set; }
    public bool FailPush { get; set; }
    public bool FailPop { get; set; }
    public int Pushes, Pops;
    public GraphicsBackend Backend => _inner.Backend;
    public GraphicsDeviceCapabilities Capabilities => _inner.Capabilities;
    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription d) => _inner.CreateProgram(d);
    public IGraphicsMesh CreateMesh(GraphicsMeshDescription d) => _inner.CreateMesh(d);
    public IGraphicsTexture2D CreateTexture2D(string l, GraphicsTextureDescription d, ReadOnlySpan<byte> data = default) => _inner.CreateTexture2D(l, d, data);
    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription d)
    {
        if (FailCreate) throw new ExpectedFailure();
        var target = new TrackedTarget(this, label, d); Targets.Add(target); return target;
    }
    public long Push(IGraphicsRenderTarget target)
    {
        if (FailPush) throw new ExpectedFailure();
        if (target is not TrackedTarget t || !ReferenceEquals(t.Device, this) || t.Disposals != 0)
            throw new InvalidOperationException("Invalid native target");
        var token = ++_sequence; _stack.Add((token, Current)); Current = t; Pushes++; return token;
    }
    public void Pop(long token)
    {
        var index = _stack.Count - 1;
        if (index < 0 || _stack[index].Token != token) throw new InvalidOperationException("Native scope order");
        if (FailPop) throw new ExpectedFailure();
        Current = _stack[index].Previous; _stack.RemoveAt(index); Pops++;
    }
    public IDisposable PushRenderTarget(IGraphicsRenderTarget target) => new Lease(this, Push(target));
    public void SetViewport(GraphicsRect v) { }
    public void SetScissor(GraphicsRect? v) { }
    public void Clear(GraphicsClearFlags f, Vec4 c) { }
    public void SetDepthState(GraphicsDepthState s) { }
    public void SetBlendMode(GraphicsBlendMode s) { }
    public void SetRasterizerState(GraphicsRasterizerState s) { }
    public void BindTexture(int slot, IGraphicsTexture2D t) { }
    public void Draw(IGraphicsMesh m) => _inner.Draw(m);
    public void Draw(IGraphicsMesh m, int count, int firstVertex = 0) => _inner.Draw(m, count, firstVertex);
    public void Draw(int count, GraphicsPrimitiveTopology t, int firstVertex = 0) => _inner.Draw(count, t, firstVertex);
    public void Dispose() => _inner.Dispose();
    sealed class Lease(TargetDevice device, long token) : IDisposable
    { public void Dispose() => device.Pop(token); }
}
sealed class ScopedTargetDevice : TargetDevice, IGraphicsRenderTargetScopeDevice
{
    public long PushRenderTargetState(IGraphicsRenderTarget target) => Push(target);
    public void PopRenderTargetState(long token) => Pop(token);
}
sealed class TrackedTarget(TargetDevice device, string label, GraphicsRenderTargetDescription description) : IGraphicsRenderTarget
{
    public GraphicsRenderTargetDescription Description => description;
    public IGraphicsDevice Device => device;
    public string Label => label;
    public int Width => description.Width;
    public int Height => description.Height;
    public IGraphicsTexture2D? ColorTexture => null;
    public IGraphicsTexture2D? DepthTexture => null;
    public int Disposals;
    public void Dispose() { if (++Disposals != 1) throw new InvalidOperationException("Double native disposal"); }
}
sealed class ExpectedFailure : Exception { }
