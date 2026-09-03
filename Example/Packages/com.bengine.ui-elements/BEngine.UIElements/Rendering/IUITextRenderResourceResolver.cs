using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface IUITextRenderResourceResolver
{
    bool TryResolveText(
        string text,
        int width,
        int height,
        float fontSize,
        out UIRenderTextureData texture);
}
