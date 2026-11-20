namespace BEngine.Editor;
public sealed class SpritePreviewProvider : IAssetPreviewProvider
{
    public bool CanPreview(BObject asset) => asset is Sprite;
    public AssetPreviewImage? GetPreview(BObject asset) { var s=(Sprite)asset; return string.IsNullOrWhiteSpace(s.Texture) ? null : EditorAssetPreviewSources.GetImage(EditorAssetPreviewSources.ResolveReference(s.Texture,s.sourcePath,typeof(Texture))); }
    public string GetIcon(BObject asset) => EditorBuiltinIcons.Assets.Image;
    public string GetInfo(BObject asset) { var s=(Sprite)asset; return $"Sprite | {s.Texture} | Pivot {s.PivotX:0.###}, {s.PivotY:0.###}"; }
    public void Draw(BObject asset, Rect area) { if(GetPreview(asset) is {} image) AssetPreview.DrawImage(area,image); else GUI.Label(area,"Sprite texture unavailable"); }
}
