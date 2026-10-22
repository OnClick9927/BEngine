using BEngine.Serialization;

namespace BEngine;

public sealed class AnimatorState
{
    private AnimationClip? _clip;
    private string _clipPath = string.Empty;
    private long _clipGeneration = -1;
    public string name { get; set; } = "New State";
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string clipPath
    {
        get => !string.IsNullOrWhiteSpace(_clip?.assetPath) ? _clip.assetPath : _clipPath;
        set { _clipPath = value ?? string.Empty; _clip = null; _clipGeneration = -1; }
    }
    [FormerlySerializedAs("clipPath")]
    public AnimationClip? clip
    {
        get
        {
            if (_clip is { isDestroyed: false }) return _clip;
            if (string.IsNullOrWhiteSpace(_clipPath)) return null;
            if (_clipGeneration != BAsset.CacheGeneration || _clip?.isDestroyed == true)
            { _clip = BAsset.Load<AnimationClip>(_clipPath); _clipGeneration = BAsset.CacheGeneration; }
            return _clip is { isDestroyed: false } ? _clip : null;
        }
        set { _clip = value; _clipPath = value?.assetPath ?? string.Empty; _clipGeneration = BAsset.CacheGeneration; }
    }
    public AnimatorBlendTree? blendTree { get; set; }
    public Fix64 speed { get; set; } = Fix64.One;
    public bool loop { get; set; } = true;
    public Vector2? editorPosition { get; set; }
    public List<AnimatorTransition> transitions { get; set; } = [];
    internal AnimationClip? ResolveClip() => clip;
    internal IEnumerable<AnimationClip> ResolveClips()
    {
        if (blendTree is { } tree)
            return tree.ResolveClips().ToArray();
        return ResolveClip() is { } motion ? [motion] : [];
    }
}
