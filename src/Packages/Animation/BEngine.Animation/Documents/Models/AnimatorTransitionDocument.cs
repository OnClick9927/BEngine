using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
internal sealed class AnimatorTransitionData  { public string DestinationState { get; set; } = string.Empty; public bool HasExitTime { get; set; } public long ExitTime { get; set; } = Fix64.OneRaw; public long Duration { get; set; } public List<AnimatorConditionData> Conditions { get; set; } = []; }
