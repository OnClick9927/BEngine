using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
public sealed class AnimatorStateDocument : Document { public string Name { get; set; } = string.Empty; public string ClipPath { get; set; } = string.Empty; public long Speed { get; set; } = Fix64.OneRaw; public bool Loop { get; set; } = true; public List<AnimatorTransitionDocument> Transitions { get; set; } = []; }
