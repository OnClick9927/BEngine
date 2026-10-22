namespace BEngine.Editor;

public static partial class AnimationEditing
{
    public static void RenameStateMachine(AnimatorController asset, int layer, string path, string name)
    {
        var machine = asset.FindStateMachine(layer, path) ?? throw new ArgumentException("State machine not found.");
        RenameNode(asset, layer, path, name, value => machine.name = value);
    }
    public static void RemoveStateMachine(AnimatorController asset, int layer, string path)
    {
        var machine = asset.FindStateMachine(layer, path) ?? throw new ArgumentException("State machine not found.");
        RemoveNode(asset, layer, path, () => asset.GetStateMachines(layer, AnimatorController.ParentPath(path)).Remove(machine));
    }
    private static void RenameNode(AnimatorController asset, int layer, string path, string name, Action<string> rename)
    {
        name = name.Trim();
        var parent = AnimatorController.ParentPath(path);
        var next = AnimatorController.ChildPath(parent, name);
        if (next == path) return;
        if (!AnimatorController.IsValidStateName(name) || asset.FindState(layer, next) is not null || asset.FindStateMachine(layer, next) is not null)
            throw new ArgumentException("States and state machines require unique local names without '/'.");
        Edit(asset, "Rename Animator Node", () => RewriteReferences(asset, layer, path, next, () => rename(name)));
    }
    private static void RemoveNode(AnimatorController asset, int layer, string path, Action remove) =>
        Edit(asset, "Remove Animator Node", () => RewriteReferences(asset, layer, path, null, remove));

    // Resolve references before mutation: identical leaf names in separate graphs must stay separate.
    private static void RewriteReferences(AnimatorController asset, int layer, string oldPath, string? newPath, Action mutate)
    {
        bool Affected(string path) => path == oldPath || path.StartsWith(oldPath + "/", StringComparison.Ordinal);
        string Remap(string path) => Affected(path) && newPath is not null ? newPath + path[oldPath.Length..] : path;
        string Reference(string context, string target) => AnimatorController.ParentPath(target) == context ? target[(target.LastIndexOf('/') + 1)..] : "/" + target;
        var graphs = asset.GetMachinePaths(layer).Select(path => (Path: path,
            Default: asset.ResolveDestinationPath(layer, path, asset.GetDefaultState(layer, path)),
            Transitions: asset.GetTransitionLists(layer, path).SelectMany(list => list.Select(transition => (List: list, Transition: transition,
                Target: transition.isExit ? null : asset.ResolveDestinationPath(layer, path, transition.destinationState)))).ToArray())).ToArray();
        mutate();
        foreach (var graph in graphs)
        {
            if (newPath is null && Affected(graph.Path)) continue;
            var context = Remap(graph.Path);
            if (graph.Default is { } original)
            {
                var target = newPath is null && Affected(original) ? null : Remap(original);
                asset.SetDefaultState(layer, context, target is null
                    ? asset.GetStates(layer, context).FirstOrDefault()?.name ?? asset.GetStateMachines(layer, context).FirstOrDefault()?.name ?? ""
                    : Reference(context, target));
            }
            foreach (var (list, transition, originalTarget) in graph.Transitions)
            {
                if (originalTarget is null) continue;
                if (newPath is null && Affected(originalTarget)) list.Remove(transition);
                else if (Affected(originalTarget) || context != graph.Path)
                    transition.destinationState = Reference(context, Remap(originalTarget));
            }
        }
    }
}

public sealed partial class AnimatorWindowController
{
    internal const int MachineNodeStart = -1000;
    internal AnimatorStateMachine? MachineAt(int node) => node <= MachineNodeStart && Model.Asset is { } asset
        ? asset.GetStateMachines(Model.LayerIndex, Model.MachinePath).ElementAtOrDefault(MachineNodeStart - node) : null;
    internal string? NodeName(int node) => MachineAt(node)?.name ?? Model.Asset?.GetStates(Model.LayerIndex, Model.MachinePath).ElementAtOrDefault(node)?.name;
    internal List<AnimatorTransition>? NodeTransitions(int node) => Model.Asset is not { } asset ? null : node switch
    {
        -2 => asset.GetAnyStateTransitions(Model.LayerIndex, Model.MachinePath),
        -3 => asset.GetEntryTransitions(Model.LayerIndex, Model.MachinePath),
        <= MachineNodeStart => MachineAt(node)?.transitions,
        _ => asset.GetStates(Model.LayerIndex, Model.MachinePath).ElementAtOrDefault(node)?.transitions
    };
    public void Navigate(string path)
    {
        if (Model.Asset is not { } asset || path.Length > 0 && asset.FindStateMachine(Model.LayerIndex, path) is null) return;
        Model.MachinePath = path; Model.SelectedStates.Clear(); Model.StateIndex = -1;
        Model.TransitionIndex = -1; Model.TransitionSource = -100; Model.Pan = new(20, 40); Model.Zoom = 1;
        Repaint();
    }
    internal void ValidateContext()
    {
        if (Model.Asset is not { } asset) return;
        Model.LayerIndex = Math.Clamp(Model.LayerIndex, 0, asset.layerCount - 1);
        if (Model.MachinePath.Length > 0 && asset.FindStateMachine(Model.LayerIndex, Model.MachinePath) is null) Navigate("");
    }
    public void AddStateMachine(Vector2 position)
    {
        if (Model.Asset is not { } asset) return;
        if (Model.MachinePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 16)
        { Model.Message = "State machines support up to 16 nested levels."; return; }
        var children = asset.GetStateMachines(Model.LayerIndex, Model.MachinePath);
        var name = "New State Machine"; var suffix = 1;
        while (children.Any(child => child.name == name) || asset.GetStates(Model.LayerIndex, Model.MachinePath).Any(state => state.name == name)) name = "New State Machine " + suffix++;
        AnimationEditing.Edit(asset, "Add Animator State Machine", () =>
        {
            children.Add(new() { name = name, editorPosition = position });
            if (asset.GetDefaultState(Model.LayerIndex, Model.MachinePath).Length == 0) asset.SetDefaultState(Model.LayerIndex, Model.MachinePath, name);
        });
        Model.SelectedStates.Clear(); Model.StateIndex = MachineNodeStart - children.Count + 1; Model.TransitionIndex = -1;
    }
}

public sealed partial class AnimatorWindow
{
    private void OnHierarchyUndo() { controller.ValidateContext(); model.TransitionSource = NoNode; EndDrag(); Repaint(); }
    private void DrawBreadcrumbs(AnimatorController asset)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button(model.LayerIndex == 0 ? "Base Layer" : asset.layers[model.LayerIndex - 1].name, EditorStyles.toolbarButton)) controller.Navigate("");
        var path = "";
        foreach (var part in model.MachinePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = AnimatorController.ChildPath(path, part);
            GUILayout.Label(">", GUILayout.Width(12));
            if (GUILayout.Button(part, EditorStyles.toolbarButton)) { controller.Navigate(path); break; }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }
    private void DrawMachine(AnimatorController asset, AnimatorStateMachine machine)
    {
        GUILayout.Label("Sub-State Machine", EditorStyles.boldLabel);
        var name = EditorGUILayout.TextField("Name", machine.name);
        if (name != machine.name) Try(() => AnimationEditing.RenameStateMachine(asset, model.LayerIndex,
            AnimatorController.ChildPath(model.MachinePath, machine.name), name));
        if (GUILayout.Button("Open State Machine")) controller.Navigate(AnimatorController.ChildPath(model.MachinePath, machine.name));
        if (GUILayout.Button("Set as Default")) controller.SetDefault();
        if (GUILayout.Button("Make Exit Transition")) model.TransitionSource = model.StateIndex;
        GUILayout.Label("Outgoing transitions run when a child reaches Exit.");
        for (var index = 0; index < machine.transitions.Count; index++)
            if (GUILayout.Button(machine.name + " -> " + DestinationName(machine.transitions[index])))
            { model.TransitionOwner = model.StateIndex; model.TransitionIndex = index; }
        if (GUILayout.Button("Delete State Machine")) controller.DeleteSelection();
    }
}
