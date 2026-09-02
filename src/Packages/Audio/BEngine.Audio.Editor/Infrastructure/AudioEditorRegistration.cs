using BEngine.Editor;

namespace BEngine.Audio.Editor;

internal static class AudioEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void Register()
    {
        AssetTypeRegistry.Register<AudioClip>(".wav", nameof(AudioClip), LoadClip,
            EditorBuiltinIcons.Assets.Audio, typeof(AudioImporter));
        EditorIconRegistry.Register(typeof(AudioClip), EditorBuiltinIcons.Assets.Audio);
        EditorIconRegistry.Register(typeof(AudioSource), EditorBuiltinIcons.Assets.Audio);
        EditorIconRegistry.Register(typeof(AudioListener), EditorBuiltinIcons.Assets.Audio);
    }

    private static AudioClip LoadClip(AssetLoadContext context)
    {
        var importer = AssetImporter.GetAtPath(context.AssetPath) as AudioImporter;
        return AudioClip.Load(context.ImportedPath,
            importer?.forceToMono ?? false,
            importer?.normalize ?? false);
    }
}
