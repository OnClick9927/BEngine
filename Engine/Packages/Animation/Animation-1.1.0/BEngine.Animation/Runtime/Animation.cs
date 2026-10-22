using System.Reflection;

namespace BEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Animation/Animation")]
public sealed class Animation : Behaviour
{
    [FormerlySerializedAs("clipPath")]
    public AnimationClip? clip { get; set; }
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string clipPath { get => clip?.assetPath ?? string.Empty; set => clip = BAsset.Load<AnimationClip>(value); }
    public bool playAutomatically { get; set; } = true;
    public bool isPlaying { get; private set; }
    private Fix64 _time;
    private bool _started;
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public Fix64 time { get => _time; set => _time = Mathf.Max(0, value); }
    public void Play() { _started = true; isPlaying = clip is { isDestroyed: false }; _time = 0; }
    public void Stop() { _started = true; isPlaying = false; _time = 0; }
    internal void Tick(Fix64 deltaTime)
    {
        if (!_started && playAutomatically) Play();
        if (!isPlaying) return;
        if (clip is not { isDestroyed: false }) { Stop(); return; }
        _time += deltaTime;
        var sampleTime = clip.length > 0 && clip.wrapMode == WrapMode.Loop ? _time % clip.length : Mathf.Min(_time, clip.length);
        if (_time >= clip.length && clip.wrapMode != WrapMode.Loop) isPlaying = false;
        clip.SampleAnimation(gameObject, sampleTime);
    }
}
