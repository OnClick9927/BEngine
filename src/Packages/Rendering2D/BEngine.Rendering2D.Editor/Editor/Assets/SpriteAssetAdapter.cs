using System.Runtime.CompilerServices;
namespace BEngine.Editor;
internal sealed class SpriteAssetAdapter : EditorAssetAdapter
{
    [ModuleInitializer] internal static void Register() => EditorAssetAdapters.Register(new SpriteAssetAdapter());
    public override Type AssetType => typeof(Texture);
    public override bool MayHaveRepresentations => true;
    public override IEnumerable<BObject> GetRepresentations(BObject asset) => CreateImportedSprite((Texture)asset) is { } sprite ? [sprite] : [];
    public override void Invalidate() => SpriteSourceRegistry.Invalidate();
    internal static Sprite? CreateImportedSprite(BEngine.Texture texture)
    {
        if (AssetImporter.GetAtPath(texture.assetPath) is not TextureImporter
            {
                textureType: TextureImporterType.Sprite
            } importer) return null;
        var sprite = Sprite.FromTexture(texture.assetPath,
            new Vector2(importer.spritePivotX, importer.spritePivotY), texture.assetPath);
        sprite.BindAssetFile(texture.assetPath, texture.sourcePath, texture.Id, nameof(Sprite));
        var parentGuid = RuntimeAssetReferences.TryGetSubAssetIdentity(texture, out var owner, out _)
            ? owner : texture.Id;
        sprite.BindSubAssetReference(texture.assetPath, parentGuid, 21300000, texture.Id);
        AssetDatabase.BindSubAsset(sprite, new AssetDatabase.SubAssetBinding(texture.assetPath, texture.assetPath,
            parentGuid, 21300000, false, true));
        return sprite;
    }

}
