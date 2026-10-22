namespace BEngine;

public sealed class TimelineClip
{
    private AnimationClip? _clip;
    private string _clipPath = string.Empty;
    private BAsset? _mediaAsset;
    private string _mediaPath = string.Empty;
    private string _mediaType = string.Empty;
    private TimelineAsset? _nestedTimeline;
    private string _nestedTimelinePath = string.Empty;

    public string name { get; set; } = "Clip";
    public Fix64 start { get; set; }
    public Fix64 duration { get; set; } = Fix64.One;
    public Fix64 clipIn { get; set; }
    public Fix64 timeScale { get; set; } = Fix64.One;
    public Fix64 easeInDuration { get; set; }
    public Fix64 easeOutDuration { get; set; }
    public string signalName { get; set; } = string.Empty;
    public string cameraBinding { get; set; } = Guid.NewGuid().ToString("N");
    public bool loopNested { get; set; }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public TimelineAsset? nestedTimeline
    {
        get => _nestedTimeline is { isDestroyed: false } live ? live :
            _nestedTimelinePath.Length > 0 ? _nestedTimeline = BAsset.Load<TimelineAsset>(_nestedTimelinePath) : null;
        set { _nestedTimeline = value; _nestedTimelinePath = value?.assetPath ?? string.Empty; }
    }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string nestedTimelinePath
    {
        get => _nestedTimeline?.assetPath is { Length: > 0 } path ? path : _nestedTimelinePath;
        set { _nestedTimeline = null; _nestedTimelinePath = value?.Trim() ?? string.Empty; }
    }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public AnimationClip? clip
    {
        get => _clip is { isDestroyed: false } live ? live
            : _clipPath.Length > 0 ? _clip = BAsset.Load<AnimationClip>(_clipPath) : null;
        set { _clip = value; _clipPath = value?.assetPath ?? string.Empty; }
    }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public BAsset? mediaAsset
    {
        get
        {
            if (_mediaAsset is { isDestroyed: false } live) return live;
            if (_mediaPath.Length == 0 || _mediaType.Length == 0) return null;
            var type = RuntimeTypeCache.FindType(_mediaType);
            if (type is not null && !typeof(BAsset).IsAssignableFrom(type)) return null;
            return type is null ? null : _mediaAsset = BAsset.Load(_mediaPath, type);
        }
        set
        {
            _mediaAsset = value;
            _mediaPath = value?.assetPath ?? string.Empty;
            _mediaType = value?.GetType().FullName ?? string.Empty;
        }
    }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string clipPath
    {
        get => _clip?.assetPath is { Length: > 0 } path ? path : _clipPath;
        set { _clip = null; _clipPath = value?.Trim() ?? string.Empty; }
    }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string mediaPath
    {
        get => _mediaAsset?.assetPath is { Length: > 0 } path ? path : _mediaPath;
        set { _mediaAsset = null; _mediaPath = value?.Trim() ?? string.Empty; }
    }

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string mediaType
    {
        get => _mediaAsset?.GetType().FullName ?? _mediaType;
        set { _mediaAsset = null; _mediaType = value?.Trim() ?? string.Empty; }
    }

    public Fix64 end => start + Mathf.Max(0, duration);

    internal AnimationClip? ResolveClip() => clip;
    internal BAsset? ResolveMediaAsset() => mediaAsset;
    internal bool HasUnsavedNestedTimeline => _nestedTimeline is { isDestroyed: false } && nestedTimelinePath.Length == 0;
    internal TimelineAsset? ResolveNestedTimeline(TimelineAsset? fallback)
    {
        if (nestedTimeline is { } asset) return asset;
        if (nestedTimelinePath.Length > 0)
            throw new FileNotFoundException("Nested Timeline asset is missing: " + nestedTimelinePath, nestedTimelinePath);
        return fallback;
    }
}
