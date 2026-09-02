namespace BEngine.Editor;

/// <summary>A source-backed raster preview. Procedural previews are drawn with DrawAssetPreview.</summary>
public readonly record struct AssetPreviewImage(string Source, int Width, int Height);
