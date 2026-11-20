namespace BEngine.Editor.Rendering;

/// <summary>Resolves editor image data without requiring a window, GPU or UI toolkit.</summary>
public interface IGpuCanvasResourceResolver
{
    bool TryResolveTexture(string source, out EditorTextureData texture);
}
