using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface IUIRenderResourceResolver
{
    bool TryResolveTexture(string source, out RasterizedImage texture);
}

/// <summary>Optional imported block texture path, avoiding a CPU decode when the GPU supports the format.</summary>
public interface IUIImportedTextureResolver
{
    bool TryResolveImportedTexture(string source, out TextureImageData texture);
}
