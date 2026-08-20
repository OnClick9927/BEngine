using System.Reflection;

namespace BEngine.Animation;

[DisallowMultipleComponent]
[AddComponentMenu("Animation/Animation")]
public sealed class Animation : Behaviour
{
    public string clipPath { get; set; } = string.Empty;
    public bool playAutomatically { get; set; } = true;
    public bool isPlaying { get; private set; }
    private AnimationClip? _clip;
    private Fix64 _time;
    public void Play() { _clip ??= string.IsNullOrWhiteSpace(clipPath) ? null : AnimationClip.Load(AssetPath.Resolve(clipPath)); isPlaying = _clip is not null; _time = 0; }
    public void Stop() { isPlaying = false; _time = 0; }
    internal void Tick(Fix64 deltaTime)
    {
        if (!isPlaying && playAutomatically) Play();
        if (!isPlaying || _clip is null) return;
        _time += deltaTime;
        if (_time > _clip.length && _clip.wrapMode != WrapMode.Loop) { isPlaying = false; _time = _clip.length; }
        _clip.SampleAnimation(gameObject, _clip.length > 0 ? _time % _clip.length : 0);
    }
}
