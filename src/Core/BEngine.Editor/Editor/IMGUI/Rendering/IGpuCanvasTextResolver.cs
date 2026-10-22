using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

public interface IGpuCanvasTextResolver
{
    bool TryMeasureText(string text, float fontSize, string fontFamily, out int width)
    {
        width = 0;
        return false;
    }

    bool TryResolveText(string text, int width, int height, float fontSize, string fontFamily,
        out GpuCanvasTextureData texture);
}
