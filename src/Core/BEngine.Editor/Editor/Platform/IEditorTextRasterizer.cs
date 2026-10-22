using BEngine.Rendering;
namespace BEngine.Editor;
public interface IEditorTextRasterizer
{
    bool TryMeasure(string text, float fontSize, string fontFamily, out int width, out int height);
    bool TryMeasureAdvance(string text, float fontSize, string fontFamily, out int width);
    bool TryRasterize(string text, int width, int height, float fontSize, string fontFamily, out RasterizedImage texture, bool wordWrap = false, int padding = 0);
}
