namespace BEngine.Rendering.Rhi;

/// <summary>Optional binding cache. Native API users must invalidate it after modifying device state.</summary>
public interface IGraphicsStateCache
{
    void InvalidateState();
    GraphicsBindingStatistics BindingStatistics { get; }
}

public readonly record struct GraphicsBindingStatistics(long ProgramBinds, long UniformWrites,
    long TextureBinds, long VertexArrayBinds, long CacheHits);
