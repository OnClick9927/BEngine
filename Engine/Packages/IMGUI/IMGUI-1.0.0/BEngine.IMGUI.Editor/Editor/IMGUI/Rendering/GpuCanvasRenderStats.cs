namespace BEngine.Editor.Rendering;

public readonly record struct GpuCanvasRenderStats(
    int CommandCount,
    int VisibleCommandCount,
    int BatchCount,
    int DrawCallCount,
    int BufferUploadCount,
    int VertexCount);
