using BEngine.Editor;

namespace BEngine.Audio.Editor;

public sealed class AudioImporter : AssetImporter
{
    public bool forceToMono { get; set; }
    public bool normalize { get; set; }
    public bool loadInBackground { get; set; } = true;
    public bool preloadAudioData { get; set; } = true;

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        forceToMono = Get(settings, nameof(forceToMono), false);
        normalize = Get(settings, nameof(normalize), false);
        loadInBackground = Get(settings, nameof(loadInBackground), true);
        preloadAudioData = Get(settings, nameof(preloadAudioData), true);
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(forceToMono), forceToMono);
        Set(settings, nameof(normalize), normalize);
        Set(settings, nameof(loadInBackground), loadInBackground);
        Set(settings, nameof(preloadAudioData), preloadAudioData);
    }
}
