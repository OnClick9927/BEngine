using BEngine.Serialization;

namespace BEngine;
internal sealed class AnimatorTransitionData  { public string DestinationState { get; set; } = string.Empty; public bool IsExit { get; set; } public bool HasFixedDuration { get; set; } public long Offset { get; set; } public bool CanTransitionToSelf { get; set; } = true; public bool HasExitTime { get; set; } public long ExitTime { get; set; } = Fix64.OneRaw; public long Duration { get; set; } public List<AnimatorConditionData> Conditions { get; set; } = []; }
