using BEngine.Rendering;
namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorTextRasterizer through dependency injection.</summary>
public sealed class EditorTextUtility
{
    private readonly IEditorTextRasterizer _implementation;

    public EditorTextUtility(IEditorTextRasterizer implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static EditorTextUtility Current => EditorServices.GetRequiredService<EditorTextUtility>();

    public bool TryMeasure(string text, float fontSize, string fontFamily, out int width, out int height) =>
        _implementation.TryMeasure(text, fontSize, fontFamily, out width, out height);

    public bool TryMeasureAdvance(string text, float fontSize, string fontFamily, out int width) =>
        _implementation.TryMeasureAdvance(text, fontSize, fontFamily, out width);

    public bool TryRasterize(string text, int width, int height, float fontSize, string fontFamily, out RasterizedImage texture, bool wordWrap = false, int padding = 0) =>
        _implementation.TryRasterize(text, width, height, fontSize, fontFamily, out texture, wordWrap, padding);
}
