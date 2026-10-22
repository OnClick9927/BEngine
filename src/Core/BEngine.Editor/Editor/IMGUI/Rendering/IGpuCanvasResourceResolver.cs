using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

public interface IGpuCanvasResourceResolver
{
    bool TryResolveTexture(string source, out GpuCanvasTextureData texture);
}
