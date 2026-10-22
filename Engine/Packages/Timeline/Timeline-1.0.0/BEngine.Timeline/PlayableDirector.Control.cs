namespace BEngine;

public sealed partial class PlayableDirector
{
    private static readonly HashSet<PlayableDirector> EvaluationStack = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlayableDirector, ControlledState> _controlled = new(ReferenceEqualityComparer.Instance);
    private PlayableDirector? _controlOwner;
    internal bool isControlled => _controlOwner is not null;
    internal void CollectControlled(HashSet<PlayableDirector> targets)
    {
        foreach (var child in _controlled.Keys)
            if (targets.Add(child)) child.CollectControlled(targets);
    }

    internal IEnumerable<(PlayableDirector Director, TimelineAsset? Asset)> ControlInputs(TimelineAsset? asset)
    {
        if (asset is null || asset.isDestroyed) yield break;
        foreach (var track in asset.tracks)
            if (!track.muted && track.type == TimelineTrackType.Control && track.clips.Count > 0 &&
                GetGenericBinding(track.id)?.GetComponent<PlayableDirector>() is { isDestroyed: false } target)
                foreach (var clip in track.clips)
                    yield return (target, clip.ResolveNestedTimeline(target.playableAsset));
    }

    private void EvaluateControls(Dictionary<PlayableDirector, TimelineClip> desired)
    {
        foreach (var previous in _controlled.Keys.Where(item => !desired.ContainsKey(item) || item.isDestroyed).ToArray())
        {
            var state = _controlled[previous]; _controlled.Remove(previous);
            RestoreControl(previous, state);
        }
        foreach (var (child, clip) in desired)
        {
            if (EvaluationStack.Contains(child) || EvaluationStack.Count >= 64)
                throw new InvalidOperationException("Cyclic or excessively nested Timeline control binding.");
            if (child._controlOwner is { } owner && !ReferenceEquals(owner, this))
                throw new InvalidOperationException("The nested director is already controlled by another Timeline.");
            if (!_controlled.TryGetValue(child, out var saved))
            {
                saved = new(child.playableAsset, child._time, child._started, child._isPlaying,
                    child._paused, child._runtimeDriven, child.wrapLoop, child._effectiveAsset is not null);
                child.ResetEvaluationState();
                _controlled.Add(child, saved);
                child._controlOwner = this;
            }
            var nested = clip.ResolveNestedTimeline(saved.Asset);
            child.playableAsset = nested;
            child._started = true; child._isPlaying = _isPlaying; child._paused = !_isPlaying;
            child._runtimeDriven = _runtimeDriven;
            child.wrapLoop = clip.loopNested;
            var local = (_time - clip.start) * clip.timeScale + clip.clipIn;
            var length = nested?.duration ?? Fix64.Zero;
            child._time = length > 0 && clip.loopNested ? Mathf.Repeat(local, length) : Fix64.Clamp(local, 0, length);
            child.Evaluate();
        }
    }

    private void ReleaseControls()
    {
        var released = _controlled.ToArray(); _controlled.Clear();
        foreach (var (child, state) in released) RestoreControl(child, state);
    }

    private static void RestoreControl(PlayableDirector child, ControlledState state)
    {
        child._controlOwner = null;
        if (child.isDestroyed) return;
        child.ResetEvaluationState();
        child.playableAsset = state.Asset;
        child._time = state.Time; child._started = state.Started; child._isPlaying = state.Playing;
        child._paused = state.Paused; child._runtimeDriven = state.RuntimeDriven; child.wrapLoop = state.Loop;
        if (state.Evaluated) child.Evaluate();
    }

    private sealed record ControlledState(TimelineAsset? Asset, Fix64 Time, bool Started, bool Playing,
        bool Paused, bool RuntimeDriven, bool Loop, bool Evaluated);
}
