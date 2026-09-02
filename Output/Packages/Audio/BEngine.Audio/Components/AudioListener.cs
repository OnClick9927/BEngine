namespace BEngine.Audio;

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Audio Listener")]
public sealed class AudioListener : MonoBehaviour
{
    private static Fix64 _volume = Fix64.One;

    [Range(0, 1)]
    public static Fix64 volume
    {
        get => _volume;
        set => _volume = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
    }

    public static bool pause { get; set; }
}
