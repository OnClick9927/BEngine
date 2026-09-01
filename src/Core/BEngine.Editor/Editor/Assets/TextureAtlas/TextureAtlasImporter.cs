namespace BEngine.Editor;

/// <summary>Imports an atlas manifest and maintains its generated Texture in Library/Artifacts.</summary>
public sealed class TextureAtlasImporter : AssetImporter
{
    public override void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var atlas = TextureAtlas.Load(context.sourcePath);
        TextureAtlasBuilder.Build(atlas, context.sourcePath, refreshAssets: false);
        context.CopySourceToArtifact();
    }
}
