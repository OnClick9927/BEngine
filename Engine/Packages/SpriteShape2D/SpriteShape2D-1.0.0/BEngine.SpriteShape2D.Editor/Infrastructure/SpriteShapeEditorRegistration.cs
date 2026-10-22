using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class SpriteShapeEditorRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package editor registration.")]
    internal static void Register()
    {
        AssetTypeRegistry.Register<SpriteShapeAsset>(".spriteshape.yaml", "Sprite Shape",
            context => SpriteShapeAsset.Load(context.SourcePath), EditorBuiltinIcons.Assets.Image);
        EditorIconRegistry.Register(typeof(SpriteShapeRenderer2D), EditorBuiltinIcons.Assets.Image);
    }
}
