using BEngine.Serialization;

namespace BEngine;
internal sealed class AnimatorControllerData
{
    public string Format { get; set; } = "BEngine.AnimatorController"; public int Version { get; set; } = 2;
    public ComponentGraphData? Graph { get; set; }
    public string Name { get; set; } = "New Animator Controller"; public string DefaultState { get; set; } = string.Empty;
    public long EntryX { get; set; } = ((Fix64)40).RawValue; public long EntryY { get; set; } = ((Fix64)140).RawValue;
    public long AnyX { get; set; } = ((Fix64)40).RawValue; public long AnyY { get; set; } = ((Fix64)40).RawValue;
    public long ExitX { get; set; } = ((Fix64)560).RawValue; public long ExitY { get; set; } = ((Fix64)40).RawValue;
    public List<AnimatorTransitionData> EntryTransitions { get; set; } = [];
    public List<AnimatorTransitionData> AnyStateTransitions { get; set; } = [];
    public List<AnimatorLayerData> Layers { get; set; } = [];
    public List<AnimatorParameterData> Parameters { get; set; } = []; public List<AnimatorStateData> States { get; set; } = [];
}

internal sealed class AnimatorLayerData
{
    public string Name { get; set; } = "New Layer";
    public long Weight { get; set; } = Fix64.OneRaw;
    public AnimatorControllerData StateMachine { get; set; } = new();
}
