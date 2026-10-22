namespace BEngine;

public sealed partial class Animator
{
    // The common single-clip state needs neither an array nor a boxed enumerator.
    private readonly struct MotionSet
    {
        private readonly AnimationClip? _single;
        private readonly IReadOnlyList<(AnimationClip Clip, Fix64 Weight)>? _many;
        internal MotionSet(AnimationClip single) => _single = single;
        internal MotionSet(IReadOnlyList<(AnimationClip Clip, Fix64 Weight)> many) => _many = many;
        internal int Length => _many?.Count ?? (_single is null ? 0 : 1);
        internal (AnimationClip Clip, Fix64 Weight) this[int index] => _many is not null ? _many[index] : (_single!, Fix64.One);
        public Enumerator GetEnumerator() => new(this);
        internal struct Enumerator(MotionSet source)
        {
            private int _index = -1;
            public bool MoveNext() => ++_index < source.Length;
            public (AnimationClip Clip, Fix64 Weight) Current => source[_index];
        }
    }

    private void SamplePose()
    {
        try { _pose.SampleWeightedClips(gameObject, _samples, applyRootMotion); }
        finally { _samples.Clear(); }
    }

    private void SampleReference(AnimationClip clip)
    {
        _samples.Clear(); _samples.Add((clip, Fix64.Zero, Fix64.One)); SamplePose();
    }

    private void EvaluateLayer(int layer, AnimatorController controller, int synchronizedSource, Fix64 deltaTime)
    {
        var previous = _playback; var previousLayer = _activeLayer; var previousWeight = _layerWeight;
        if (!_playbacks.TryGetValue(layer, out var playback)) _playbacks[layer] = playback = new Playback();
        _playback = playback; _activeLayer = layer; _layerWeight = GetLayerWeight(layer);
        try
        {
            if (synchronizedSource >= 0) SampleSynchronizedLayer(controller, synchronizedSource);
            else TickCurrentLayer(controller, deltaTime);
        }
        finally { _playback = previous; _activeLayer = previousLayer; _layerWeight = previousWeight; }
    }

    private bool TryTransition(AnimatorController controller, string context, Fix64 normalized,
        out AnimatorStateRouter.Route? route, out AnimatorTransition? selected)
    {
        if (TryAnyTransition(controller, context, normalized, out route, out selected)) return true;
        if (_fromState is null)
            foreach (var transition in _state!.transitions)
                if (TryFollowTransition(controller, context, transition, normalized, out route))
                { selected = transition; return true; }
        return false;
    }

    private bool TryAnyTransition(AnimatorController controller, string context, Fix64 normalized,
        out AnimatorStateRouter.Route? route, out AnimatorTransition? selected)
    {
        // Parent Any State transitions take precedence over nested machines.
        if (context.Length > 0 && TryAnyTransition(controller, AnimatorController.ParentPath(context), normalized, out route, out selected)) return true;
        foreach (var transition in controller.GetAnyStateTransitions(_activeLayer, context))
            if (TryFollowTransition(controller, context, transition, normalized, out route))
            { selected = transition; return true; }
        route = null; selected = null; return false;
    }

    private bool TryFollowTransition(AnimatorController controller, string context, AnimatorTransition transition,
        Fix64 normalized, out AnimatorStateRouter.Route? route)
    {
        route = null;
        if (transition.hasExitTime && normalized < transition.exitTime) return false;
        foreach (var condition in transition.conditions) if (!Evaluate(condition)) return false;
        route = new AnimatorStateRouter(controller, _activeLayer, Evaluate).Follow(context, transition);
        return route is not null && (transition.canTransitionToSelf || route.State is null || route.Path != _playback.StatePath);
    }
}
