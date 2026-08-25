namespace BEngine.Rendering.Rhi;

/// <summary>Defers resource disposal until commands that reference it have completed.</summary>
public interface IGraphicsResourceRetirement
{
    void RetireResource(IDisposable resource);
}
