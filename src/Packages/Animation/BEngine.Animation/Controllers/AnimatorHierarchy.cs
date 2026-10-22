namespace BEngine;

public sealed partial class AnimatorController
{
    public List<AnimatorStateMachine> GetStateMachines(int layer, string machinePath = "") => machinePath.Length == 0
        ? layer == 0 ? stateMachines : layers[layer - 1].stateMachines : RequireMachine(layer, machinePath).stateMachines;
    public List<AnimatorState> GetStates(int layer, string machinePath) => machinePath.Length == 0 ? GetStates(layer) : RequireMachine(layer, machinePath).states;
    public List<AnimatorTransition> GetEntryTransitions(int layer, string machinePath) => machinePath.Length == 0 ? GetEntryTransitions(layer) : RequireMachine(layer, machinePath).entryTransitions;
    public List<AnimatorTransition> GetAnyStateTransitions(int layer, string machinePath) => machinePath.Length == 0 ? GetAnyStateTransitions(layer) : RequireMachine(layer, machinePath).anyStateTransitions;
    public string GetDefaultState(int layer, string machinePath) => machinePath.Length == 0 ? GetDefaultState(layer) : RequireMachine(layer, machinePath).defaultState;
    public void SetDefaultState(int layer, string machinePath, string value)
    { if (machinePath.Length == 0) SetDefaultState(layer, value); else RequireMachine(layer, machinePath).defaultState = value; }
    public AnimatorStateMachine? FindStateMachine(int layer, string path)
    {
        var children = layer == 0 ? stateMachines : layers[layer - 1].stateMachines;
        AnimatorStateMachine? found = null;
        foreach (var name in path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        { found = children.FirstOrDefault(item => item.name == name); if (found is null) return null; children = found.stateMachines; }
        return found;
    }
    private AnimatorStateMachine RequireMachine(int layer, string path) => FindStateMachine(layer, path)
        ?? throw new ArgumentException($"Animator state machine '{path}' does not exist.", nameof(path));
    public AnimatorState? FindState(int layer, string path)
    {
        path = path.Trim('/');
        var parent = ParentPath(path);
        var states = parent.Length == 0 ? GetStates(layer) : FindStateMachine(layer, parent)?.states;
        if (states is not null)
        {
            var name = path.AsSpan(path.LastIndexOf('/') + 1);
            foreach (var state in states) if (name.SequenceEqual(state.name)) return state;
        }
        return null;
    }
    public IEnumerable<string> GetMachinePaths(int layer)
    {
        yield return string.Empty;
        var visited = new HashSet<AnimatorStateMachine>();
        foreach (var path in Walk(GetStateMachines(layer), "", 1)) yield return path;
        IEnumerable<string> Walk(List<AnimatorStateMachine> children, string parent, int depth)
        {
            if (depth > 16 && children.Count > 0) throw new InvalidDataException("Animator state machines exceed 16 levels.");
            foreach (var machine in children)
            {
                if (!visited.Add(machine) || visited.Count > 4096) throw new InvalidDataException("Animator state machines must form a bounded tree without shared children or cycles.");
                var path = ChildPath(parent, machine.name);
                yield return path;
                foreach (var child in Walk(machine.stateMachines, path, depth + 1)) yield return child;
            }
        }
    }
    public IEnumerable<AnimatorState> GetAllStates(int layer) => GetMachinePaths(layer).SelectMany(path => GetStates(layer, path));
    public string? GetStatePath(int layer, AnimatorState state)
    {
        foreach (var path in GetMachinePaths(layer))
            if (GetStates(layer, path).Contains(state)) return ChildPath(path, state.name);
        return null;
    }
    /// <summary>Transition lists owned by this graph; child-machine exits are authored in the parent graph.</summary>
    public IEnumerable<List<AnimatorTransition>> GetTransitionLists(int layer, string machinePath) =>
        GetStates(layer, machinePath).Select(state => state.transitions)
        .Concat(GetStateMachines(layer, machinePath).Select(machine => machine.transitions))
        .Prepend(GetEntryTransitions(layer, machinePath)).Prepend(GetAnyStateTransitions(layer, machinePath));
    /// <summary>Resolve a local, ../ relative, or / absolute state/machine path.</summary>
    public string? ResolveDestinationPath(int layer, string context, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return null;
        string? Normalize(string value)
        {
            var parts = new List<string>();
            foreach (var part in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
                else parts.Add(part);
            }
            return string.Join('/', parts);
        }
        bool Exists(string? path) => path is { Length: > 0 } && (FindState(layer, path) is not null || FindStateMachine(layer, path) is not null);
        var local = Normalize(destination.StartsWith('/') ? destination : ChildPath(context, destination));
        if (Exists(local)) return local;
        var absolute = Normalize(destination);
        return !destination.StartsWith('.') && Exists(absolute) ? absolute : null;
    }
    public static string ChildPath(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;
    public static string ParentPath(string path) => path.LastIndexOf('/') is var slash && slash >= 0 ? path[..slash] : string.Empty;
    public static bool IsValidStateName(string name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim() && name is not ("." or "..") && !name.Contains('/');
    public void ValidateHierarchy()
    {
        var allStates = new HashSet<AnimatorState>();
        var allMachines = new HashSet<AnimatorStateMachine>();
        for (var layer = 0; layer < layerCount; layer++)
            foreach (var path in GetMachinePaths(layer))
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var state in GetStates(layer, path))
                    if (!IsValidStateName(state.name) || !names.Add(state.name) || !allStates.Add(state))
                        throw new InvalidDataException("Animator states require unique local names and a single parent.");
                foreach (var child in GetStateMachines(layer, path))
                    if (!IsValidStateName(child.name) || !names.Add(child.name) || !allMachines.Add(child))
                        throw new InvalidDataException("Animator state machines require unique local names and a single parent.");
            }
    }
}
