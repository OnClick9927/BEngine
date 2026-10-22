using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class SpriteSkinningEditorRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package editor registration.")]
    internal static void Register()
    {
        AssetTypeRegistry.Register<SpriteSkinAsset>(".spriteskin.yaml", "Sprite Skin",
            context => SpriteSkinAsset.Load(context.SourcePath), EditorBuiltinIcons.Assets.Image);
        EditorIconRegistry.Register(typeof(SpriteSkin2D), EditorBuiltinIcons.Assets.Image);
        EditorIconRegistry.Register(typeof(Bone2D), EditorBuiltinIcons.Assets.Data);
        EditorIconRegistry.Register(typeof(LimbIK2D), EditorBuiltinIcons.Assets.Data);
    }
}
