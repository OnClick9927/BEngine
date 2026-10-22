using BEngine.Platform;
using BEngine.Editor.Rendering;
using BEngine.Rendering;

namespace BEngine.Editor;

/// <summary>Editor image and Unicode text resources. Text is rasterized once, then composited by the GPU canvas.</summary>
internal sealed class EditorGpuCanvasResourceResolver : IGpuCanvasResourceResolver, IGpuCanvasTextResolver
{
    public static EditorGpuCanvasResourceResolver Shared { get; } = new();

    public bool TryResolveTexture(string source, out GpuCanvasTextureData texture) =>
        EditorInstrumentation.TryResolveTexture(source, out texture) ||
        CanvasResourceUtility.Current.TryResolveTexture(source, out texture);

    public bool TryMeasureText(string text, float fontSize, string fontFamily, out int width)
    {
        width = 0;
        return EditorTextUtility.Current.TryMeasure(text, fontSize, fontFamily, out width, out _);
    }

    internal bool TryMeasureTextAdvance(string text, float fontSize, string fontFamily, out int width)
    {
        width = 0;
        return EditorTextUtility.Current.TryMeasureAdvance(text, fontSize, fontFamily, out width);
    }

    internal bool TryMeasureLineHeight(float fontSize, string fontFamily, out int height)
    {
        height = 0;
        return EditorTextUtility.Current.TryMeasure("Ag", fontSize, fontFamily, out _, out height);
    }

    public bool TryResolveText(string text, int width, int height, float fontSize, string fontFamily,
        out GpuCanvasTextureData texture)
    {
        texture = default;
        if (!EditorTextUtility.Current.TryRasterize(text, width, height, fontSize, fontFamily, out var data))
            return false;
        texture = new GpuCanvasTextureData(data.Width, data.Height, data.Format, data.Pixels);
        return true;
    }

}
