using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface IUIRenderResourceResolver
{
    bool TryResolveTexture(string source, out UIRenderTextureData texture);
}
