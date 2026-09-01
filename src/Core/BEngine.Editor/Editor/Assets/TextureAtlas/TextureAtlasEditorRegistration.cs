using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class TextureAtlasEditorRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        AssetTypeRegistry.Register<TextureAtlas>(".atlas.yaml", nameof(TextureAtlas),
            context => TextureAtlas.Load(context.SourcePath), EditorBuiltinIcons.Assets.Atlas,
            typeof(TextureAtlasImporter));
    }
}
