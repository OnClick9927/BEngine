using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class TextureAtlasEditorRegistration
{
    [ModuleInitializer]
    internal static void Register() =>
        AssetTypeRegistry.Register(".atlas.yaml", nameof(TextureAtlas), EditorBuiltinIcons.Assets.Image);
}
