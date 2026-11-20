using BEngine.Editor;
using System.Runtime.CompilerServices;

namespace BEngine.Audio.Editor;

internal static class AudioEditorRegistration
{
    // Importers must be registered before the first project scan. Waiting for
    // window initialization would import every audio file twice on each launch.
    [ModuleInitializer, InitializeOnLoadMethod]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package asset importer registration.")]
    internal static void Register()
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
