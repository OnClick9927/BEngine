using System.Runtime.CompilerServices;
namespace BEngine.Editor;
internal static class Rendering2DEditorAssembly
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeBuildResources.Register(typeof(Sprite).Assembly.GetName().Name!, "Shaders/PortableScene");
        EditorIconRegistry.Register(typeof(Camera2D), EditorBuiltinIcons.Components.Camera2D);
        EditorIconRegistry.Register(typeof(SpriteRenderer), EditorBuiltinIcons.Components.SpriteRenderer);
        EditorIconRegistry.Register(typeof(SpriteMask), EditorBuiltinIcons.Components.SpriteMask);
    }
}
