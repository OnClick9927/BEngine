using BEngine.Platform;
using static BEngine.Editor.EditorAssetPreviewSources;

namespace BEngine.Editor;

internal sealed class GraphicsAssetPreview(Type type) : EditorAssetAdapter, IEditorAssetPreview
{
    public override Type AssetType => type;
    public override bool ReadOnly => type != typeof(Material);
    public override IEditorAssetPreview Preview => this;
    public string GetIcon(BAsset asset) => asset switch
    {
        Material => EditorBuiltinIcons.Assets.Material,
        Shader => EditorBuiltinIcons.Assets.Shader,
        Font => EditorBuiltinIcons.Assets.Font,
        _ => EditorBuiltinIcons.Assets.Default
    };
    public string GetInfo(BAsset asset) => asset switch
    {
        Material value => $"Material | Shader: {value.shader.shaderName}",
        Shader value => $"Shader | {value.shaderName}",
        Font value => $"Font | Default size {value.defaultSize} | {value.characterSet}",
        _ => string.Empty
    };
    public EditorAssetPreviewContent GetContent(BAsset asset) => asset switch
    {
        Material value => new(EditorAssetPreviewKind.ColorSample, Title: value.shader.shaderName, Color: value.color),
        Shader value => new(EditorAssetPreviewKind.GradientIcon, GetIcon(value), value.shaderName),
        Font value => new(EditorAssetPreviewKind.IconSummary, GetIcon(value), value.name, $"Font | Default size {value.defaultSize}"),
        _ => default
    };
}

/// <summary>Source-dependent classification remains with the graphics formats, not with the GUI.</summary>
internal sealed class GraphicsSourcePreview : EditorAssetAdapter, IEditorAssetPreview
{
    public override Type AssetType => typeof(DefaultAsset);
    public override bool ReadOnly => false;
    public override IEditorAssetPreview Preview => this;
    private static bool IsImage(DefaultAsset asset) =>
        asset.assetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase) ||
        asset.assetType.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
        BEngine.Rendering.ImageCodec.IsSupportedPath(SourcePath(asset));
    public bool CanPreview(BAsset asset) => asset is DefaultAsset value && (IsImage(value) ||
        value is TextAsset && value.assetType.Equals(nameof(Shader), StringComparison.OrdinalIgnoreCase));
    public AssetPreviewImage? GetImage(BAsset asset) => IsImage((DefaultAsset)asset)
        ? EditorAssetPreviewSources.GetImage(SourcePath((DefaultAsset)asset)) : null;
    public string GetIcon(BAsset asset) => IconFor((DefaultAsset)asset);
    public string GetInfo(BAsset asset)
    {
        var value = (DefaultAsset)asset;
        if (!IsImage(value)) return TextInfo(GetTextPreview((TextAsset)value), value.assetType);
        var image = GetImage(value);
        var path = SourcePath(value);
        var suffix = SafeFileSize(path);
        var format = EditorServices.GetRequiredService<PathUtility>().GetExtension(path).TrimStart('.').ToUpperInvariant();
        return image is null ? $"{value.assetType} | Preview unavailable{suffix}" :
            $"{image.Value.Width} x {image.Value.Height} | {format}{suffix}";
    }
    public EditorAssetPreviewContent GetContent(BAsset asset)
    {
        var value = (DefaultAsset)asset;
        if (!IsImage(value)) return new(EditorAssetPreviewKind.HeaderText, EditorBuiltinIcons.Assets.Shader,
            "Shader source", GetTextPreview((TextAsset)value));
        return GetImage(value) is { } image ? new(EditorAssetPreviewKind.Image, Image: image) :
            new(EditorAssetPreviewKind.Unavailable, GetIcon(value), "Image preview unavailable");
    }
}
