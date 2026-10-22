using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugMeshState(
    string Label,
    int AvailableVertexCount,
    GraphicsBufferUsage Usage,
    int StrideBytes,
    IReadOnlyList<GraphicsVertexAttribute> Attributes);
