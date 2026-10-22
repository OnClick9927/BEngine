using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class TextureAtlasEditorRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package asset importer registration.")]
    internal static void Register()
    {
        AssetTypeRegistry.Register<TextureAtlas>(".atlas.yaml", nameof(TextureAtlas),
            context => TextureAtlas.Load(context.SourcePath), "Icons/Assets/AssetAtlas.png",
            typeof(TextureAtlasImporter));
    }
}
