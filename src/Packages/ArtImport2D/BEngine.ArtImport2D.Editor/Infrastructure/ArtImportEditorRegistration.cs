using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class ArtImportEditorRegistration
{
    [ModuleInitializer, InitializeOnLoadMethod]
    internal static void Register()
    {
        Register(".psd", "Photoshop Document");
        Register(".psb", "Photoshop Large Document");
        Register(".aseprite", "Aseprite Document");
        Register(".ase", "Aseprite Document");
        AssetTypeRegistry.Register<ArtDocument2D>(".art2d.yaml", "Editable Layered Art",
            context => ArtDocument2D.LoadImported(context.SourcePath), EditorBuiltinIcons.Assets.Image);
    }

    private static void Register(string suffix, string displayName) =>
        AssetTypeRegistry.Register<ArtDocument2D>(suffix, displayName,
            context => ArtDocument2D.LoadImported(context.ImportedPath),
            EditorBuiltinIcons.Assets.Image, typeof(ArtDocumentImporter));
}
