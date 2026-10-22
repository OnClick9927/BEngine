namespace BEngine.Rendering.Rhi;

/// <summary>Disposes a resource, releasing its dependent only after all resulting native retirements finish.</summary>
public interface IGraphicsResourceDisposal
{
    void DisposeResource(IDisposable resource, IDisposable dependent);
}
