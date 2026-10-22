using BEngine.Serialization;

namespace BEngine;
internal sealed class AnimatorStateData  { public string Name { get; set; } = string.Empty; public string ClipPath { get; set; } = string.Empty; public AnimatorBlendTreeData? BlendTree { get; set; } public long Speed { get; set; } = Fix64.OneRaw; public bool Loop { get; set; } = true; public long? EditorX { get; set; } public long? EditorY { get; set; } public List<AnimatorTransitionData> Transitions { get; set; } = []; }

internal sealed class AnimatorBlendTreeData
{
    public string Parameter { get; set; } = string.Empty;
    public List<AnimatorBlendMotionData> Motions { get; set; } = [];
}

internal sealed class AnimatorBlendMotionData
{
    public string ClipPath { get; set; } = string.Empty;
    public long Threshold { get; set; }
}
