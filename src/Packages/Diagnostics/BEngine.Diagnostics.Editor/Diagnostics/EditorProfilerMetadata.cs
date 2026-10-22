namespace BEngine.Editor;

/// <summary>Allocation-free state used to size and synchronize caller-owned history buffers.</summary>
public readonly record struct EditorProfilerMetadata(
    long Version,
    bool Recording,
    int Capacity,
    int FrameCount);
