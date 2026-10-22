using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

internal record struct GpuCanvasBatch(
    GpuCanvasRect Clip,
    IGraphicsTexture2D? Texture,
    int FirstVertex,
    int VertexCount);
