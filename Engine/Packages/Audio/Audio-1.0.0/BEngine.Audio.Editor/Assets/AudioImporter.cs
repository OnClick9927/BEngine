using BEngine.Editor;
using BEngine.Build;

namespace BEngine.Audio.Editor;

public sealed class AudioImporter : AssetImporter
{
    public AudioClipLoadType loadType { get; set; }
    public bool forceToMono { get; set; }
    public bool normalize { get; set; }
    public bool loadInBackground { get; set; } = true;
    public bool preloadAudioData { get; set; } = true;
    public List<AudioImporterPlatformSettings> platformSettings { get; set; } = [];

    public override void Import(AssetImportContext context)
    {
        using var source = context.OpenSourceStream(); using var data = new MemoryStream(); source.CopyTo(data);
        context.cancellationToken.ThrowIfCancellationRequested();
        var bytes = AudioClip.BuildImportedBytes(data.ToArray(), loadType, forceToMono, normalize,
            loadInBackground, preloadAudioData, context.cancellationToken);
        context.cancellationToken.ThrowIfCancellationRequested(); context.WriteArtifact(bytes);
    }

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        loadType = Get(settings, nameof(loadType), AudioClipLoadType.DecompressOnLoad);
        forceToMono = Get(settings, nameof(forceToMono), false);
        normalize = Get(settings, nameof(normalize), false);
        loadInBackground = Get(settings, nameof(loadInBackground), true);
        preloadAudioData = Get(settings, nameof(preloadAudioData), true);
        platformSettings = Enum.GetValues<BuildTargetPlatform>().Select(platform =>
        {
            var prefix = $"platform.{platform}.";
            return new AudioImporterPlatformSettings
            {
                platform = platform, overridden = Get(settings, prefix + "overridden", false),
                loadType = Get(settings, prefix + "loadType", loadType),
                forceToMono = Get(settings, prefix + "forceToMono", forceToMono),
                normalize = Get(settings, prefix + "normalize", normalize),
                loadInBackground = Get(settings, prefix + "loadInBackground", loadInBackground),
                preloadAudioData = Get(settings, prefix + "preloadAudioData", preloadAudioData)
            };
        }).ToList();
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(loadType), loadType);
        Set(settings, nameof(forceToMono), forceToMono);
        Set(settings, nameof(normalize), normalize);
        Set(settings, nameof(loadInBackground), loadInBackground);
        Set(settings, nameof(preloadAudioData), preloadAudioData);
        var seen = new HashSet<BuildTargetPlatform>();
        foreach (var item in platformSettings)
        {
            if (item is null || !Enum.IsDefined(item.platform) || !seen.Add(item.platform) || !Enum.IsDefined(item.loadType))
                throw new InvalidDataException("Audio platform settings contain an invalid or repeated platform.");
            var prefix = $"platform.{item.platform}.";
            Set(settings, prefix + "overridden", item.overridden);
            Set(settings, prefix + "loadType", item.loadType);
            Set(settings, prefix + "forceToMono", item.forceToMono);
            Set(settings, prefix + "normalize", item.normalize);
            Set(settings, prefix + "loadInBackground", item.loadInBackground);
            Set(settings, prefix + "preloadAudioData", item.preloadAudioData);
        }
    }
}

[Serializable]
public sealed class AudioImporterPlatformSettings
{
    public BuildTargetPlatform platform { get; set; }
    public bool overridden { get; set; }
    public AudioClipLoadType loadType { get; set; }
    public bool forceToMono { get; set; }
    public bool normalize { get; set; }
    public bool loadInBackground { get; set; } = true;
    public bool preloadAudioData { get; set; } = true;
}
