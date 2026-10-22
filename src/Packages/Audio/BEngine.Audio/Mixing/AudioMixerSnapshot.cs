namespace BEngine.Audio;

[CreateAssetMenu(fileName = "Audio Mixer Snapshot", menuName = "Audio/Mixer Snapshot", order = 321)]
public sealed class AudioMixerSnapshot : ScriptableObject
{
    public List<AudioMixerSnapshotEntry> groups { get; set; } = [];
    public void Validate()
    {
        if (groups is null || groups.Count > 4096) throw new InvalidDataException("Invalid mixer snapshot size.");
        using var budget = LoopExecutionGuard.AllowIterations(groups.Count + 1);
        var seen = new HashSet<AudioMixerGroup>(ReferenceEqualityComparer.Instance);
        foreach (var entry in groups)
            if (entry is null || entry.group is not { isDestroyed: false } || !seen.Add(entry.group))
                throw new InvalidDataException("Snapshot groups must be alive, non-null and unique.");
    }
}

public sealed class AudioMixerSnapshotEntry
{
    public AudioMixerGroup? group { get; set; }
    [Range(0, 1)] public Fix64 volume { get; set; } = Fix64.One;
    public bool mute { get; set; }
}

/// <summary>Scene-local mixer state. Transitions never change the source group or snapshot assets.</summary>
[AddComponentMenu("Audio/Audio Mixer Controller")]
public sealed class AudioMixerController : MonoBehaviour
{
    public AudioMixerSnapshot? initialSnapshot { get; set; }
    private readonly Dictionary<AudioMixerGroup, Fix64> _volumes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Transition> _transitions = [];
    private Fix64 _elapsed;
    private Fix64 _duration;
    [HideInInspector] public bool isTransitioning => _transitions.Count > 0;

    public override void Awake() { if (initialSnapshot is not null) TransitionTo(initialSnapshot, Fix64.Zero); }
    public override void Update() => Advance(Time.deltaTime);
    public override void OnDestroy() => ResetMixer();

    public void TransitionTo(AudioMixerSnapshot snapshot, Fix64 duration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (duration < Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        snapshot.Validate();
        var requested = snapshot.groups.ToDictionary(entry => entry.group!, entry => entry.mute ? Fix64.Zero : Mathf.Clamp01(entry.volume));
        var pending = new List<Transition>();
        using var budget = LoopExecutionGuard.AllowIterations(_volumes.Count + requested.Count + 1);
        // A group absent from the new snapshot returns to its authored volume.
        foreach (var group in _volumes.Keys.Concat(requested.Keys).Distinct())
        {
            if (group.isDestroyed) continue;
            var target = requested.GetValueOrDefault(group, Mathf.Clamp01(group.volume));
            pending.Add(new Transition(group, _volumes.GetValueOrDefault(group, Mathf.Clamp01(group.volume)), target, requested.ContainsKey(group)));
        }
        _transitions.Clear(); _transitions.AddRange(pending);
        _elapsed = Fix64.Zero; _duration = duration;
        if (duration == Fix64.Zero) Advance(Fix64.Zero);
    }

    public void Advance(Fix64 deltaTime)
    {
        if (deltaTime < Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (_transitions.Count == 0) return;
        _elapsed = Fix64.Min(_duration, _elapsed + deltaTime);
        var factor = _duration <= Fix64.Zero ? Fix64.One : _elapsed / _duration;
        using var budget = LoopExecutionGuard.AllowIterations(_transitions.Count + 1);
        foreach (var transition in _transitions)
        {
            if (transition.Group.isDestroyed || factor == Fix64.One && !transition.KeepOverride) _volumes.Remove(transition.Group);
            else _volumes[transition.Group] = Mathf.Lerp(transition.From, transition.To, factor);
        }
        if (factor == Fix64.One) _transitions.Clear();
    }

    public Fix64 GetEffectiveVolume(AudioMixerGroup? group)
    {
        var gain = Fix64.One;
        for (var current = group; current is not null; current = current.parent)
        {
            if (current.isDestroyed || current.mute) return Fix64.Zero;
            gain *= _volumes.GetValueOrDefault(current, Mathf.Clamp01(current.volume));
        }
        return gain;
    }

    public Fix64 GetVolume(AudioMixerGroup group) => group.isDestroyed || group.mute ? Fix64.Zero :
        _volumes.GetValueOrDefault(group, Mathf.Clamp01(group.volume));

    public void ResetMixer() { _volumes.Clear(); _transitions.Clear(); _elapsed = _duration = Fix64.Zero; }
    private readonly record struct Transition(AudioMixerGroup Group, Fix64 From, Fix64 To, bool KeepOverride);
}
