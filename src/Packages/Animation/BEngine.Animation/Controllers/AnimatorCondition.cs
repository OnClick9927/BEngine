using BEngine.Serialization;

namespace BEngine.Animation;

public sealed class AnimatorCondition
{
    public string parameter { get; set; } = string.Empty;
    public AnimatorConditionMode mode { get; set; } = AnimatorConditionMode.If;
    public Fix64 threshold { get; set; }
}
