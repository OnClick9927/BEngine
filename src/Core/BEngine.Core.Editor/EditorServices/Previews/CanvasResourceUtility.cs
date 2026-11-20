namespace BEngine.Editor.Rendering;

/// <summary>Shared editor image sources, independent of a particular UI renderer.</summary>
public sealed class CanvasResourceUtility
{
    private readonly IGpuCanvasResourceResolver _implementation;

    public CanvasResourceUtility(IGpuCanvasResourceResolver implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static CanvasResourceUtility Current => BEngine.Editor.EditorServices.GetRequiredService<CanvasResourceUtility>();

    public bool TryResolveTexture(string source, out EditorTextureData texture) =>
        _implementation.TryResolveTexture(source, out texture);

    /// <summary>Resolves presentation overrides first, then the current source backend.</summary>
    public bool TryResolvePresentationTexture(string source, out EditorTextureData texture) =>
        EditorInstrumentation.TryResolveTexture(source, out texture) ||
        _implementation.TryResolveTexture(source, out texture);
}
