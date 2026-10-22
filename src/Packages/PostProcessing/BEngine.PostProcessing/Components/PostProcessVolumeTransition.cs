namespace BEngine.PostProcessing;

/// <summary>Animates a volume's influence, allowing profiles to transition without modifying shared assets.</summary>
[AddComponentMenu("Rendering/Post-process Volume Transition")]
public sealed class PostProcessVolumeTransition : MonoBehaviour
{
    public PostProcessVolume? volume { get; set; }
    public bool useUnscaledTime { get; set; }
    [NonSerialized] private Fix64 _start, _target, _duration, _elapsed;
    public bool isTransitioning { get; private set; }
    public event Action? completed;

    public void FadeTo(Fix64 targetWeight, Fix64 duration)
    {
        if (duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
        volume ??= GetComponent<PostProcessVolume>();
        if (volume is null) throw new InvalidOperationException("A PostProcessVolume is required.");
        _start = volume.weight; _target = Mathf.Clamp01(targetWeight); _duration = duration; _elapsed = 0;
        isTransitioning = true;
        if (duration == 0) Advance(0);
    }

    public void Cancel() => isTransitioning = false;
    public override void OnDisable() => Cancel();
    public override void Update() => Advance(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

    public void Advance(Fix64 deltaTime)
    {
        if (deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!isTransitioning || volume is null) return;
        _elapsed += deltaTime;
        var t = _duration == 0 ? Fix64.One : Mathf.Clamp01(_elapsed / _duration);
        volume.weight = Mathf.Lerp(_start, _target, t * t * (3 - 2 * t));
        if (t < 1) return;
        isTransitioning = false;
        completed?.Invoke();
    }
}
