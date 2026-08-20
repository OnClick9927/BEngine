using BEngine.Serialization;

namespace BEngine.Animation;

public sealed class AnimatorState
{
    public string name { get; set; } = "New State";
    public string clipPath { get; set; } = string.Empty;
    [HideInInspector]
    public AnimationClip? clip { get; set; }
    public Fix64 speed { get; set; } = Fix64.One;
    public bool loop { get; set; } = true;
    public List<AnimatorTransition> transitions { get; set; } = [];
    internal AnimationClip? ResolveClip() => clip ?? (!string.IsNullOrWhiteSpace(clipPath) && File.Exists(AssetPath.Resolve(clipPath)) ? AnimationClip.Load(AssetPath.Resolve(clipPath)) : null);
}
