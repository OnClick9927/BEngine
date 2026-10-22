namespace BEngine;

/// <summary>A nested state machine. Outgoing transitions run when one of its children exits.</summary>
public sealed class AnimatorStateMachine
{
    public string name { get; set; } = "New State Machine";
    public string defaultState { get; set; } = string.Empty;
    public List<AnimatorState> states { get; set; } = [];
    public List<AnimatorStateMachine> stateMachines { get; set; } = [];
    public List<AnimatorTransition> entryTransitions { get; set; } = [];
    public List<AnimatorTransition> anyStateTransitions { get; set; } = [];
    public List<AnimatorTransition> transitions { get; set; } = [];
    public Vector2? editorPosition { get; set; }
    public Vector2 entryPosition { get; set; } = new(40, 140);
    public Vector2 anyStatePosition { get; set; } = new(40, 40);
    public Vector2 exitPosition { get; set; } = new(560, 40);
}
