using BEngine.Rendering;
using BEngine.Editor.Rendering;
namespace BEngine.Editor;
internal sealed class UIElementsEditorResourceResolver : IUIRenderResourceResolver, ITextRasterizer
{
    internal static UIElementsEditorResourceResolver Shared { get; } = new();
    private bool TryResolveTexture(string source, out GpuCanvasTextureData texture) => EditorGpuCanvasResourceResolver.Shared.TryResolveTexture(source, out texture);
    bool IUIRenderResourceResolver.TryResolveTexture(string source, out RasterizedImage texture)
    {
        if (TryResolveTexture(source, out var editorTexture))
        {
            texture = new RasterizedImage(editorTexture.Width, editorTexture.Height,
                editorTexture.Format, editorTexture.Pixels);
            return true;
        }
        return FileUIRenderResourceResolver.Shared.TryResolveTexture(source, out texture);
    }

    bool ITextRasterizer.TryResolveText(
        string text, int width, int height, float fontSize, out RasterizedImage texture) =>
        ((ITextRasterizer)this).TryResolveText(text, width, height, fontSize, false, out texture);

    bool ITextRasterizer.TryResolveText(
        string text, int width, int height, float fontSize, bool wordWrap, out RasterizedImage texture)
    {
        texture = default;
        return EditorTextUtility.Current.TryRasterize(text, width, height, fontSize, "Microsoft YaHei UI",
                   out texture, wordWrap, Math.Min(6, Math.Max(0, (int)Math.Round(fontSize / 3))));
    }
}
