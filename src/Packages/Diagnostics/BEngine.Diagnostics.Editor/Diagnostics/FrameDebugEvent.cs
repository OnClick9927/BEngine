using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugEvent(
    int Index,
    FrameDebugEventKind Kind,
    string Name,
    FrameDebugRenderState State,
    string MeshLabel,
    GraphicsPrimitiveTopology Topology,
    int FirstVertex,
    int VertexCount,
    int TriangleCount,
    int LineCount,
    GraphicsClearFlags ClearFlags,
    NVector4 ClearColor,
    bool Executed,
    string Error,
    FrameDebugMarker Marker,
    FrameDebugMeshState? Mesh = null);
