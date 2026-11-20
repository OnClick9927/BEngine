namespace BEngine.Editor;

/// <summary>Font rasterizer included in a Web Player.</summary>
public enum WebFontBackend
{
    /// <summary>Complete Skia text rendering, including COLRv1 and SVG fonts.</summary>
    Skia = 0,
    /// <summary>Compact FreeType text rendering for outline, bitmap and COLRv0 fonts.</summary>
    FreeType = 1
}

