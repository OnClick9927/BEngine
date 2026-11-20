using BEngine.Platform;
using BEngine.Rendering;
namespace BEngine.Editor;
internal sealed class WindowsEditorTextRasterizer : IEditorTextRasterizer
{
    public bool TryMeasure(string text, float fontSize, string fontFamily, out int width, out int height) => WindowsTextRasterizer.TryMeasure(text, fontSize, fontFamily, out width, out height);
    public bool TryMeasureAdvance(string text, float fontSize, string fontFamily, out int width) => WindowsTextRasterizer.TryMeasureAdvance(text, fontSize, fontFamily, out width);
    public bool TryRasterize(string text, int width, int height, float fontSize, string fontFamily, out RasterizedImage texture, bool wordWrap = false, int padding = 0) => WindowsTextRasterizer.TryRasterize(text, width, height, fontSize, fontFamily, out texture, wordWrap, padding);
}
