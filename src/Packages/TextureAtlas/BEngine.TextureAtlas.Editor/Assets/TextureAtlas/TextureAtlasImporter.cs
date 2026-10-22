namespace BEngine.Editor;

/// <summary>Imports an atlas manifest and maintains its generated Texture in Library/Artifacts.</summary>
public sealed class TextureAtlasImporter : AssetImporter
{
    public override void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var atlas = TextureAtlas.Load(context.sourcePath);
        // Prepacked atlases contain regions and a texture, but no authoring
        // sources. Import their validated manifest without clearing that data.
        if (atlas.Sources.Length > 0 || atlas.Sprites.Count == 0)
            TextureAtlasBuilder.Build(atlas, context.sourcePath, refreshAssets: false);
        context.CopySourceToArtifact();
    }
}
