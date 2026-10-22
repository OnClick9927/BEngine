namespace BEngine;

/// <summary>Build a route before consuming triggers or changing playback. Invalid/cyclic routes leave playback intact.</summary>
internal sealed class AnimatorStateRouter(AnimatorController controller, int layer, Func<AnimatorCondition, bool> evaluate)
{
    internal sealed record Route(AnimatorState? State, string Path, Fix64 Offset, List<AnimatorTransition> Taken);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
    private readonly List<AnimatorTransition> _taken = [];
    internal Route? Enter(string machinePath) => EnterMachine(machinePath, 0);
    internal Route? Resolve(string context, string destination, Fix64 offset) => Destination(context, destination, offset);
    internal Route? Follow(string context, AnimatorTransition transition) => FollowTransition(context, transition, transition.offset);
    private Route? Destination(string context, string destination, Fix64 offset)
    {
        var path = controller.ResolveDestinationPath(layer, context, destination);
        if (path is null) return null;
        if (controller.FindState(layer, path) is { } state) return new(state, path, offset, [.. _taken]);
        return EnterMachine(path, offset);
    }
    private Route? EnterMachine(string path, Fix64 offset)
    {
        if (_visiting.Count >= 64 || !_visiting.Add("enter:" + path)) return null;
        foreach (var entry in controller.GetEntryTransitions(layer, path))
        {
            if (!entry.conditions.All(evaluate)) continue;
            var count = _taken.Count;
            var route = FollowTransition(path, entry, entry.offset);
            if (route is not null) { _visiting.Remove("enter:" + path); return route; }
            _taken.RemoveRange(count, _taken.Count - count);
        }
        var fallback = Destination(path, controller.GetDefaultState(layer, path), offset);
        _visiting.Remove("enter:" + path);
        return fallback;
    }
    private Route? FollowTransition(string context, AnimatorTransition transition, Fix64 offset)
    {
        _taken.Add(transition);
        return transition.isExit ? Exit(context, offset) : Destination(context, transition.destinationState, offset);
    }
    private Route? Exit(string path, Fix64 offset)
    {
        if (path.Length == 0) return new(null, "", offset, [.. _taken]);
        if (_visiting.Count >= 64 || !_visiting.Add("exit:" + path)) return null;
        var parent = AnimatorController.ParentPath(path);
        foreach (var transition in controller.FindStateMachine(layer, path)!.transitions)
        {
            if (!transition.conditions.All(evaluate)) continue;
            var count = _taken.Count;
            var route = FollowTransition(parent, transition, transition.offset);
            if (route is not null) { _visiting.Remove("exit:" + path); return route; }
            _taken.RemoveRange(count, _taken.Count - count);
        }
        var fallback = Exit(parent, offset);
        _visiting.Remove("exit:" + path);
        return fallback;
    }
}
