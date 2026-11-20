namespace BEngine.Editor;

/// <summary>Generic presentation primitives; asset-domain decisions belong to their providers.</summary>
public enum EditorAssetPreviewKind
{
    None, Image, Unavailable, ColorSample, GradientIcon, Text, HeaderText, IconSummary, Label
}

/// <summary>A transient description, consumed by the active editor UI without retaining its asset.</summary>
public readonly record struct EditorAssetPreviewContent(
    EditorAssetPreviewKind Kind, string Icon = "", string Title = "", string Text = "",
    AssetPreviewImage Image = default, Color Color = default);

/// <summary>UI-independent asset preview policy contributed by the asset owner.</summary>
public interface IEditorAssetPreview
{
    bool CanPreview(BAsset asset) => true;
    AssetPreviewImage? GetImage(BAsset asset) => null;
    string GetIcon(BAsset asset);
    string GetInfo(BAsset asset);
    EditorAssetPreviewContent GetContent(BAsset asset);
}
