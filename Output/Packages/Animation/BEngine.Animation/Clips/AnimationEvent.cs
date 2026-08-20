using BEngine.Serialization;

namespace BEngine.Animation;

public sealed class AnimationEvent
{
    public Fix64 time { get; set; }
    public string functionName { get; set; } = string.Empty;
    public string stringParameter { get; set; } = string.Empty;
    public int intParameter { get; set; }
    public Fix64 floatParameter { get; set; }
}
