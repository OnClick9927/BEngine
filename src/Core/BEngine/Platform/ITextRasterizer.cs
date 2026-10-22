namespace BEngine.Rendering;

public interface ITextRasterizer
{
    /// <summary>Creates a rasterizer for an installed family when the platform supports font authoring.</summary>
    ITextRasterizer? ForFontFamily(string family) => null;

    /// <summary>Total horizontal space reserved by the rasterizer around the measured glyph advance.</summary>
    float GetHorizontalPadding(float fontSize) => 0;

    bool TryMeasureText(string text, float fontSize, out float width, out float height)
    {
        width = height = 0;
        return false;
    }

    bool TryResolveText(
        string text,
        int width,
        int height,
        float fontSize,
        out RasterizedImage texture);

    bool TryResolveText(string text, int width, int height, float fontSize, bool wordWrap,
        out RasterizedImage texture) => TryResolveText(text, width, height, fontSize, out texture);
}
