using BEngine.Serialization;

namespace BEngine;

[CreateAssetMenu(fileName = "New Animator Controller", menuName = "Animation/Animator Controller", order = 111)]
[EditorIcon("Icons/AssetAnimation.png")]
public sealed partial class AnimatorController : ScriptableObject
{
    public string defaultState { get; set; } = string.Empty;
    public List<AnimatorControllerParameter> parameters { get; set; } = [];
    public List<AnimatorState> states { get; set; } = [];
    public List<AnimatorStateMachine> stateMachines { get; set; } = [];
    public List<AnimatorTransition> entryTransitions { get; set; } = [];
    public List<AnimatorTransition> anyStateTransitions { get; set; } = [];
    /// <summary>Additional override layers. The existing states/defaultState fields define layer zero.</summary>
    public List<AnimatorControllerLayer> layers { get; set; } = [];
    public Vector2 entryPosition { get; set; } = new(40, 140);
    public Vector2 anyStatePosition { get; set; } = new(40, 40);
    public Vector2 exitPosition { get; set; } = new(560, 40);
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public int layerCount => layers.Count + 1;
    public List<AnimatorState> GetStates(int layer) => layer == 0 ? states : layers[layer - 1].states;
    public List<AnimatorTransition> GetAnyStateTransitions(int layer) => layer == 0 ? anyStateTransitions : layers[layer - 1].anyStateTransitions;
    public List<AnimatorTransition> GetEntryTransitions(int layer) => layer == 0 ? entryTransitions : layers[layer - 1].entryTransitions;
    public string GetDefaultState(int layer) => layer == 0 ? defaultState : layers[layer - 1].defaultState;
    public void SetDefaultState(int layer, string value) { if (layer == 0) defaultState = value; else layers[layer - 1].defaultState = value; }
    public IEnumerable<AnimatorTransition> GetAllTransitions() => Enumerable.Range(0, layerCount)
        .SelectMany(layer => GetMachinePaths(layer).SelectMany(path => GetTransitionLists(layer, path).SelectMany(list => list)));
    public AnimatorState? FindState(string stateName) => FindState(0, stateName);
    public void AddState(AnimatorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsValidStateName(state.name) || states.Any(item => item.name == state.name) || stateMachines.Any(item => item.name == state.name))
            throw new InvalidOperationException($"Animator state '{state.name}' requires a unique local name without '/'.");
        states.Add(state);
        if (string.IsNullOrWhiteSpace(defaultState)) defaultState = state.name;
    }
    public void Save(string path) => AnimatorControllerSerialization.Save(this, path);
    public static AnimatorController Load(string path) => AnimatorControllerSerialization.Load(path);
}
