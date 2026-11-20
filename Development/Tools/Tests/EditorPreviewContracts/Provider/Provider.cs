using BEngine;
using BEngine.Editor;

public sealed class CollectiblePreviewProvider:IAssetPreviewProvider
{
 public bool CanPreview(BObject asset)=>asset.name=="collectible-preview-contract";
 public AssetPreviewImage? GetPreview(BObject asset)=>null;
 public string GetIcon(BObject asset)=>"collectible-preview-icon";
 public string GetInfo(BObject asset)=>"collectible-preview-info";
 public void Draw(BObject asset,Rect area){}
}
