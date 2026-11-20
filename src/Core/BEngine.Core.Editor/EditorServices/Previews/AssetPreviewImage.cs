namespace BEngine.Editor;

/// <summary>A source-backed raster preview independent of any editor UI.</summary>
[RuntimeTypeAlias("BEngine.Editor.AssetPreviewImage, BEngine.Editor")]
[RuntimeTypeAlias("BEngine.Editor.AssetPreviewImage, BEngine.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
[RuntimeTypeAlias("BEngine.Editor.AssetPreviewImage, BEngine.IMGUI.Editor")]
[RuntimeTypeAlias("BEngine.Editor.AssetPreviewImage, BEngine.IMGUI.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public readonly record struct AssetPreviewImage(string Source, int Width, int Height);
