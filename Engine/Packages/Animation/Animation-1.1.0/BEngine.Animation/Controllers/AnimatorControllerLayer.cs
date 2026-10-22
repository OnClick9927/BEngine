namespace BEngine;

public enum AnimatorLayerBlendingMode { Override, Additive }

public sealed class AnimatorLayerMaskEntry
{
    public string path { get; set; } = string.Empty;
    public bool includeChildren { get; set; } = true;
    public bool active { get; set; } = true;
}

/// <summary>An independently evaluated state machine blended over the preceding layer.</summary>
public sealed class AnimatorControllerLayer
{
    public string name { get; set; } = "New Layer";
    public Fix64 defaultWeight { get; set; } = Fix64.One;
    public AnimatorLayerBlendingMode blendingMode { get; set; }
    /// <summary>Additive reference at time zero; null uses the authored object pose.</summary>
    public AnimationClip? referencePose { get; set; }
    /// <summary>A preceding layer to follow, or -1 for an independent state machine.</summary>
    public int syncedLayerIndex { get; set; } = -1;
    public List<AnimatorLayerMaskEntry> mask { get; set; } = [];
    public string defaultState { get; set; } = string.Empty;
    public List<AnimatorState> states { get; set; } = [];
    public List<AnimatorStateMachine> stateMachines { get; set; } = [];
    public List<AnimatorTransition> entryTransitions { get; set; } = [];
    public List<AnimatorTransition> anyStateTransitions { get; set; } = [];
    public Vector2 entryPosition { get; set; } = new(40, 140);
    public Vector2 anyStatePosition { get; set; } = new(40, 40);
    public Vector2 exitPosition { get; set; } = new(560, 40);

    internal bool Includes(string relativePath)
    {
        if (mask.Count == 0) return true;
        var included = false;
        foreach (var entry in mask)
        {
            var path = entry.path.Trim('/');
            if (relativePath != path && !(entry.includeChildren && (path.Length == 0 || relativePath.StartsWith(path + "/", StringComparison.Ordinal)))) continue;
            if (!entry.active) return false;
            included = true;
        }
        return included;
    }
}
