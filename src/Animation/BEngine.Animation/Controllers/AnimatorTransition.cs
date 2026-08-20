using BEngine.Serialization;

namespace BEngine.Animation;

public sealed class AnimatorTransition
{
    public string destinationState { get; set; } = string.Empty;
    public bool hasExitTime { get; set; }
    public Fix64 exitTime { get; set; } = Fix64.One;
    public Fix64 duration { get; set; } = Fix64.Parse("0.25");
    public List<AnimatorCondition> conditions { get; set; } = [];
}
