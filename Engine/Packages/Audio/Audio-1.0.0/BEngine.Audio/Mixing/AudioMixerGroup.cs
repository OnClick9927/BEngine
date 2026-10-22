namespace BEngine.Audio;

[CreateAssetMenu(fileName = "Audio Mixer Group", menuName = "Audio/Mixer Group", order = 320)]
public sealed class AudioMixerGroup : ScriptableObject
{
    private AudioMixerGroup? _parent;
    [Range(0, 1)] public Fix64 volume { get; set; } = Fix64.One;
    public bool mute { get; set; }
    public List<AudioMixerEffect> effects { get; set; } = [];
    public List<AudioMixerSend> sends { get; set; } = [];
    public AudioMixerGroup? parent
    {
        get => _parent;
        set
        {
            var previous = _parent; _parent = value;
            try { Validate(); }
            catch { _parent = previous; throw; }
        }
    }
    public Fix64 effectiveVolume => mute ? Fix64.Zero : Mathf.Clamp01(volume) * (_parent?.effectiveVolume ?? Fix64.One);
    public void Validate() => AudioMixerRouting.Validate([this]);
}
