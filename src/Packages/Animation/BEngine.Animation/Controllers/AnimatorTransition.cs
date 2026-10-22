using BEngine.Serialization;

namespace BEngine;

public sealed class AnimatorTransition
{
    public string destinationState { get; set; } = string.Empty;
    public bool isExit { get; set; }
    public bool hasFixedDuration { get; set; }
    public Fix64 offset { get; set; }
    public bool canTransitionToSelf { get; set; } = true;
    public bool hasExitTime { get; set; }
    public Fix64 exitTime { get; set; } = Fix64.One;
    public Fix64 duration { get; set; } = Fix64.Parse("0.25");
    public List<AnimatorCondition> conditions { get; set; } = [];
}
