using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class TextureAtlasEditorRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        AssetTypeRegistry.Register<Sprite>(".sprite.yaml", nameof(Sprite),
            context => Sprite.Load(context.SourcePath), EditorBuiltinIcons.Assets.Image);
        AssetTypeRegistry.Register<TextureAtlas>(".atlas.yaml", nameof(TextureAtlas),
            context => TextureAtlas.Load(context.SourcePath), EditorBuiltinIcons.Assets.Image);
    }
}
