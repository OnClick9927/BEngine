namespace BEngine;

internal sealed class TimelineAssetData
{
    public string Format { get; set; } = "BEngine.Timeline";
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "New Timeline";
    public List<TimelineTrackData> Tracks { get; set; } = [];
}

internal sealed class TimelineTrackData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Track";
    public TimelineTrackType Type { get; set; }
    public bool Muted { get; set; }
    public bool Locked { get; set; }
    public bool ControlActivation { get; set; } = true;
    public List<TimelineClipData> Clips { get; set; } = [];
}

internal sealed class TimelineClipData
{
    public string Name { get; set; } = "Clip";
    public long Start { get; set; }
    public long Duration { get; set; } = Fix64.OneRaw;
    public long ClipIn { get; set; }
    public long TimeScale { get; set; } = Fix64.OneRaw;
    public long EaseInDuration { get; set; }
    public long EaseOutDuration { get; set; }
    public string ClipPath { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string SignalName { get; set; } = string.Empty;
    public string NestedTimelinePath { get; set; } = string.Empty;
    public string CameraBinding { get; set; } = Guid.NewGuid().ToString("N");
    public bool LoopNested { get; set; }
}

internal static class TimelineAssetSerialization
{
    internal static TimelineAsset Load(string path) => Document<TimelineAsset>
        .Read(path, source => Restore(YamlUtility.Load<TimelineAssetData>(source))).ToAsset();

    internal static TimelineAsset Deserialize(string yaml) =>
        Restore(YamlUtility.Deserialize<TimelineAssetData>(yaml));

    internal static void Save(TimelineAsset asset, string path) =>
        Document<TimelineAsset>.FromAsset(asset).Write(path,
            static (source, destination) => YamlUtility.Save(Capture(source), destination));

    private static TimelineAssetData Capture(TimelineAsset asset)
    {
        Validate(asset);
        return new TimelineAssetData
        {
            Name = asset.name,
            Tracks = [.. asset.tracks.Select(track => new TimelineTrackData
            {
                Id = track.id, Name = track.name, Type = track.type,
                Muted = track.muted, Locked = track.locked, ControlActivation = track.controlActivation,
                Clips = [.. track.clips.Select(clip => new TimelineClipData
                {
                    Name = clip.name, Start = clip.start.RawValue,
                    Duration = clip.duration.RawValue, ClipIn = clip.clipIn.RawValue,
                    TimeScale = clip.timeScale.RawValue,
                    EaseInDuration = clip.easeInDuration.RawValue,
                    EaseOutDuration = clip.easeOutDuration.RawValue,
                    ClipPath = clip.clipPath, MediaPath = clip.mediaPath,
                    MediaType = clip.mediaType,
                    SignalName = clip.signalName, NestedTimelinePath = clip.nestedTimelinePath,
                    CameraBinding = clip.cameraBinding, LoopNested = clip.loopNested
                })]
            })]
        };
    }

    private static TimelineAsset Restore(TimelineAssetData data)
    {
        if (data.Format != "BEngine.Timeline" || data.Version is < 1 or > 2)
            throw new InvalidDataException("Unsupported Timeline asset.");
        var asset = new TimelineAsset
        {
            name = data.Name,
            tracks = [.. data.Tracks.Select(track => new TimelineTrack
            {
                id = track.Id, name = track.Name, type = track.Type,
                muted = track.Muted, locked = track.Locked, controlActivation = track.ControlActivation,
                clips = [.. track.Clips.Select(clip => new TimelineClip
                {
                    name = clip.Name, start = Fix64.FromRaw(clip.Start),
                    duration = Fix64.FromRaw(clip.Duration),
                    clipIn = Fix64.FromRaw(clip.ClipIn),
                    timeScale = Fix64.FromRaw(clip.TimeScale),
                    easeInDuration = Fix64.FromRaw(clip.EaseInDuration),
                    easeOutDuration = Fix64.FromRaw(clip.EaseOutDuration),
                    clipPath = clip.ClipPath, mediaPath = clip.MediaPath,
                    mediaType = clip.MediaType,
                    signalName = clip.SignalName, nestedTimelinePath = clip.NestedTimelinePath,
                    cameraBinding = clip.CameraBinding, loopNested = clip.LoopNested
                })]
            })]
        };
        Validate(asset);
        return asset;
    }

    private static void Validate(TimelineAsset asset)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in asset.tracks)
        {
            if (string.IsNullOrWhiteSpace(track.id) || !ids.Add(track.id))
                throw new InvalidDataException("Timeline track IDs must be unique and nonempty.");
            if (!Enum.IsDefined(track.type))
                throw new InvalidDataException($"Unsupported Timeline track type {track.type}.");
            foreach (var clip in track.clips)
            {
                if (clip.start < 0 || clip.duration < 0 || clip.clipIn < 0 || clip.timeScale <= 0 ||
                    clip.easeInDuration < 0 || clip.easeOutDuration < 0)
                    throw new InvalidDataException("Timeline clip times must be nonnegative and timeScale must be positive.");
                if (clip.clip is not null && clip.clipPath.Length == 0)
                    throw new InvalidOperationException("Save an AnimationClip before referencing it from a Timeline.");
                if (clip.mediaAsset is not null && clip.mediaPath.Length == 0)
                    throw new InvalidOperationException("Save a media asset before referencing it from a Timeline.");
                if (clip.HasUnsavedNestedTimeline)
                    throw new InvalidOperationException("Save a nested Timeline before referencing it from a Timeline.");
                if (track.type == TimelineTrackType.Camera && string.IsNullOrWhiteSpace(clip.cameraBinding))
                    throw new InvalidDataException("Camera clips require a binding key.");
            }
        }
    }
}
