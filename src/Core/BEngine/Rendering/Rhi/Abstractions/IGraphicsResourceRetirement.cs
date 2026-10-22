namespace BEngine.Rendering.Rhi;

/// <summary>Defers resource disposal until commands that reference it have completed.</summary>
public interface IGraphicsResourceRetirement
{
    void RetireResource(IDisposable resource);
}

/// <summary>Optional explicit pressure recovery. Ordinary retirement need not wait for the GPU.</summary>
public interface IGraphicsResourceRetirementMaintenance
{
    /// <summary>Waits for outstanding device frames and drains safe retirements, or returns false while a frame is recording.</summary>
    bool TryFlushRetiredResources();
}
