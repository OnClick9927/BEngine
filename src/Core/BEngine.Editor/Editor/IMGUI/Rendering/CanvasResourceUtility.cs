using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

/// <summary>Application-facing API; the platform host supplies IGpuCanvasResourceResolver through dependency injection.</summary>
public sealed class CanvasResourceUtility
{
    private readonly IGpuCanvasResourceResolver _implementation;

    public CanvasResourceUtility(IGpuCanvasResourceResolver implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static CanvasResourceUtility Current => BEngine.Editor.EditorServices.GetRequiredService<CanvasResourceUtility>();

    public bool TryResolveTexture(string source, out GpuCanvasTextureData texture) =>
        _implementation.TryResolveTexture(source, out texture);
}
