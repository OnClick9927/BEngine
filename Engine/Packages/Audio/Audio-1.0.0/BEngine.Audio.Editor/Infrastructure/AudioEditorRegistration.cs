using BEngine.Editor;

namespace BEngine.Audio.Editor;

internal static class AudioEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void Register()
    {
        foreach (var extension in new[] { ".wav", ".mp3", ".ogg" })
            AssetTypeRegistry.Register<AudioClip>(extension, nameof(AudioClip), LoadClip,
                "Icons/Assets/AssetAudio.png", typeof(AudioImporter));
        EditorIconRegistry.Register(typeof(AudioClip), "Icons/Assets/AssetAudio.png");
        EditorIconRegistry.Register(typeof(AudioSource), "Icons/Assets/AssetAudio.png");
        EditorIconRegistry.Register(typeof(AudioListener), "Icons/Assets/AssetAudio.png");
    }

    private static AudioClip LoadClip(AssetLoadContext context)
    {
        return AudioClip.Load(context.ImportedPath);
    }
}
