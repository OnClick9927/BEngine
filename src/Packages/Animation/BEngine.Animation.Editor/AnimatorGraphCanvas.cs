using System.Globalization;
using BEngine.Editor.UIElements;
using BEngine.UIElements;

namespace BEngine.Editor;

public sealed partial class AnimatorWindow
{
    private const int NoNode = -100, AnyNode = -2, EntryNode = -3, ExitNode = -4;
    private static readonly UIColor WireColor = new(171, 176, 184);
    private static readonly UIColor DefaultColor = new(64, 179, 94);
    private NodeCanvas? _graphCanvas;
    private bool _frameRequested;
    private bool _autoFramed;
    private bool _applyingFrame;
    private Rect _graphViewport;

    private NodeCanvas GraphCanvas
    {
        get
        {
            if (_graphCanvas is not null) return _graphCanvas;
            var canvas = new NodeCanvas();
            canvas.ViewChanged += (pan, zoom) =>
            {
                model.Pan = pan;
                model.Zoom = zoom;
                if (!_applyingFrame) _autoFramed = false;
                Repaint();
            };
            canvas.SelectionChanged += OnCanvasSelectionChanged;
            canvas.NodeClicked += OnCanvasNodeClicked;
            canvas.NodeDoubleClicked += OnCanvasNodeDoubleClicked;
            canvas.NodeDragStarted += () =>
            {
                if (!EditorAssetWritePolicy.CanWrite || model.Asset is not { } asset) return;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(asset, "Move Animator States");
            };
            canvas.NodesMoved += positions =>
            {
                if (!EditorAssetWritePolicy.CanWrite || model.Asset is not { } asset) return;
                foreach (var (id, position) in positions)
                    if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var node))
                        SetPosition(asset, node, position);
                EditorUtility.SetDirty(asset);
                Repaint();
            };
            canvas.ContextRequested += (nodeId, edgeId, position) =>
            {
                if (model.Asset is not { } asset) return;
                var node = int.TryParse(nodeId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) ? parsed : NoNode;
                if (node != NoNode) edgeId = null;
                if (TryParseConnection(edgeId, out var owner, out var index))
                { model.TransitionOwner = owner; model.TransitionIndex = index; }
                else if (edgeId is not null) model.TransitionIndex = -1;
                ShowGraphMenu(node, position, edgeId);
            };
            rootVisualElement.Add(canvas);
            _graphCanvas = canvas;
            return canvas;
        }
    }

    private Vector2 NodePosition(AnimatorController asset, int node)
    {
        if (controller.MachineAt(node) is { } machine) return machine.editorPosition ?? new Vector2(250, 240);
        if (node >= 0)
            return asset.GetStates(model.LayerIndex, model.MachinePath)[node].editorPosition ??
                   new Vector2(250 + node % 3 * 215, 100 + node / 3 * 110);
        if (asset.FindStateMachine(model.LayerIndex, model.MachinePath) is { } scope)
            return node == AnyNode ? scope.anyStatePosition : node == EntryNode ? scope.entryPosition : scope.exitPosition;
        var layer = model.LayerIndex > 0 ? asset.layers[model.LayerIndex - 1] : null;
        return node == AnyNode ? layer?.anyStatePosition ?? asset.anyStatePosition
            : node == EntryNode ? layer?.entryPosition ?? asset.entryPosition
            : layer?.exitPosition ?? asset.exitPosition;
    }

    private void SetPosition(AnimatorController asset, int node, Vector2 position)
    {
        if (controller.MachineAt(node) is { } machine) { machine.editorPosition = position; return; }
        if (node >= 0)
        {
            if (node < asset.GetStates(model.LayerIndex, model.MachinePath).Count)
                asset.GetStates(model.LayerIndex, model.MachinePath)[node].editorPosition = position;
            return;
        }
        if (asset.FindStateMachine(model.LayerIndex, model.MachinePath) is { } scope)
        {
            if (node == AnyNode) scope.anyStatePosition = position;
            else if (node == EntryNode) scope.entryPosition = position;
            else if (node == ExitNode) scope.exitPosition = position;
        }
        else if (model.LayerIndex > 0)
        {
            var layer = asset.layers[model.LayerIndex - 1];
            if (node == AnyNode) layer.anyStatePosition = position;
            else if (node == EntryNode) layer.entryPosition = position;
            else if (node == ExitNode) layer.exitPosition = position;
        }
        else if (node == AnyNode) asset.anyStatePosition = position;
        else if (node == EntryNode) asset.entryPosition = position;
        else if (node == ExitNode) asset.exitPosition = position;
    }

    private Rect NodeRect(AnimatorController asset, int node)
    {
        var position = NodePosition(asset, node) * model.Zoom + model.Pan;
        return new Rect(position.x, position.y, (node >= 0 || node <= AnimatorWindowController.MachineNodeStart ? 175 : 120) * model.Zoom,
            (node >= 0 || node <= AnimatorWindowController.MachineNodeStart ? 50 : 32) * model.Zoom);
    }

    private static (Vector2 start, Vector2 end) Connection(Rect source, Rect destination)
    {
        var (start, end) = NodeCanvasGeometry.Connection(source, destination);
        return (start, end);
    }

    private void DrawGraph(AnimatorController asset, Rect viewport)
    {
        _graphViewport = viewport;
        uiElementContainer.position = viewport;
        uiElementContainer.visible = true;
        var canvas = GraphCanvas;
        canvas.allowEditing = EditorAssetWritePolicy.CanWrite;
        var size = new Vector2(viewport.width, viewport.height);
        var resized = canvas.viewport != size;
        canvas.SetViewport(size);
        canvas.SetView(model.Pan, model.Zoom);
        var states = asset.GetStates(model.LayerIndex, model.MachinePath);
        model.SelectedStates.RemoveWhere(index => index < 0 || index >= states.Count);
        if (model.TransitionSource >= states.Count) model.TransitionSource = NoNode;
        var runtimeAnimator = RuntimeAnimator(asset);
        var runtime = runtimeAnimator?.GetCurrentStateName(model.LayerIndex);
        var nodes = new List<NodeCanvasNode>(states.Count + 3)
        {
            new(GraphId(AnyNode), "Any State", NodePosition(asset, AnyNode), new Vector2(120, 32),
                new UIColor(51, 115, 133)),
            new(GraphId(EntryNode), "Entry", NodePosition(asset, EntryNode), new Vector2(120, 32),
                new UIColor(56, 128, 77)),
            new(GraphId(ExitNode), "Exit", NodePosition(asset, ExitNode), new Vector2(120, 32),
                new UIColor(128, 56, 59))
        };
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            var color = AnimatorController.ChildPath(model.MachinePath, state.name) == runtime ? new UIColor(49, 122, 166)
                : state.name == asset.GetDefaultState(model.LayerIndex, model.MachinePath) ? new UIColor(166, 94, 33)
                : new UIColor(77, 77, 79);
            var progress = AnimatorController.ChildPath(model.MachinePath, state.name) == runtime && runtimeAnimator is not null
                ? Mathf.Repeat(runtimeAnimator.GetNormalizedTime(model.LayerIndex), 1) : Fix64.Zero;
            nodes.Add(new NodeCanvasNode(GraphId(index), state.name, NodePosition(asset, index),
                new Vector2(175, 50), color,
                state.blendTree is { } tree ? "Blend Tree: " + tree.parameter : state.clip?.name ?? "(No Motion)",
                model.SelectedStates.Contains(index), progress));
        }
        var machines = asset.GetStateMachines(model.LayerIndex, model.MachinePath);
        for (var index = 0; index < machines.Count; index++)
        {
            var machine = machines[index]; var id = AnimatorWindowController.MachineNodeStart - index;
            var path = AnimatorController.ChildPath(model.MachinePath, machine.name);
            var active = runtime?.StartsWith(path + "/", StringComparison.Ordinal) == true;
            nodes.Add(new NodeCanvasNode(GraphId(id), machine.name, NodePosition(asset, id), new Vector2(175, 50),
                active ? new UIColor(49, 122, 166) : machine.name == asset.GetDefaultState(model.LayerIndex, model.MachinePath)
                    ? new UIColor(166, 94, 33) : new UIColor(82, 64, 112), "Sub-State Machine", model.StateIndex == id));
        }
        int Target(string destination)
        {
            var path = asset.ResolveDestinationPath(model.LayerIndex, model.MachinePath, destination);
            if (path is null) return NoNode;
            var prefix = model.MachinePath.Length == 0 ? "" : model.MachinePath + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return ExitNode;
            var local = path[prefix.Length..].Split('/')[0];
            var stateIndex = states.FindIndex(state => state.name == local);
            if (stateIndex >= 0) return stateIndex;
            var machineIndex = machines.FindIndex(machine => machine.name == local);
            return machineIndex >= 0 ? AnimatorWindowController.MachineNodeStart - machineIndex : ExitNode;
        }
        var connections = new List<NodeCanvasConnection>();
        var defaultNode = Target(asset.GetDefaultState(model.LayerIndex, model.MachinePath));
        if (defaultNode != NoNode) connections.Add(new NodeCanvasConnection("default", GraphId(EntryNode), GraphId(defaultNode), DefaultColor));
        var owners = Enumerable.Range(0, states.Count).Concat(Enumerable.Range(0, machines.Count).Select(index => AnimatorWindowController.MachineNodeStart - index)).Prepend(EntryNode).Prepend(AnyNode);
        foreach (var owner in owners)
        {
            var transitions = controller.NodeTransitions(owner)!;
            for (var index = 0; index < transitions.Count; index++)
            {
                var transition = transitions[index];
                var target = transition.isExit ? ExitNode : Target(transition.destinationState);
                if (target == NoNode) continue;
                connections.Add(new NodeCanvasConnection($"{owner}:{index}", GraphId(owner), GraphId(target),
                    WireColor, model.TransitionOwner == owner && model.TransitionIndex == index));
            }
        }
        canvas.SetGraph(nodes, connections);
        var selected = model.SelectedStates.Select(GraphId).ToList();
        if (model.StateIndex is AnyNode or EntryNode or ExitNode || controller.MachineAt(model.StateIndex) is not null) selected.Add(GraphId(model.StateIndex));
        canvas.SetSelection(selected, model.TransitionIndex >= 0
            ? $"{model.TransitionOwner}:{model.TransitionIndex}" : null);
        if (_frameRequested || resized && _autoFramed)
        {
            _frameRequested = false;
            _applyingFrame = true;
            try { canvas.FrameAll(); _autoFramed = true; }
            finally { _applyingFrame = false; }
        }
        var runtimeInfo = runtimeAnimator?.GetPlaybackInfo(model.LayerIndex);
        var runtimeCaption = runtimeAnimator is null || string.IsNullOrWhiteSpace(runtimeInfo?.StateName)
            ? string.Empty
            : "    ▶ " + runtimeInfo.Value.StateName +
              (runtimeInfo.Value.InTransition ? $"  ({runtimeInfo.Value.TransitionNormalizedTime:0.##})" :
                  $"  {runtimeInfo.Value.NormalizedTime:0.##}");
        canvas.SetCaption(asset.name + "  >  " +
            (model.LayerIndex == 0 ? "Base Layer" : asset.layers[model.LayerIndex - 1].name)
            + (model.MachinePath.Length == 0 ? "" : " > " + model.MachinePath)
            + "    " + (int)(canvas.zoom * 100) + "%"
            + runtimeCaption
            + (model.TransitionSource == NoNode ? string.Empty : "    Click a destination state or Exit"));
        ProcessGraphDragAndDrop(asset, viewport);
        ProcessGraphKeyboard(asset);
    }

    private static string GraphId(int node) => node.ToString(CultureInfo.InvariantCulture);

    private void OnCanvasSelectionChanged(IReadOnlyCollection<string> selected, string? connection)
    {
        model.GraphFocused = true;
        model.SidebarFocused = false;
        model.SelectedStates.Clear();
        foreach (var id in selected)
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0) model.SelectedStates.Add(index);
        if (TryParseConnection(connection, out var owner, out var transition))
        {
            model.TransitionOwner = owner;
            model.TransitionIndex = transition;
            model.StateIndex = -1;
        }
        else
        {
            model.TransitionIndex = -1;
            var special = selected.Select(id => int.TryParse(id, out var value) ? value : NoNode)
                .FirstOrDefault(value => value < 0 && value != NoNode);
            model.StateIndex = special != 0 ? special : model.SelectedStates.FirstOrDefault(-1);
        }
        Repaint();
    }

    private void OnCanvasNodeClicked(string id)
    {
        if (!EditorAssetWritePolicy.CanWrite || model.TransitionSource == NoNode || _graphCanvas is null) return;
        if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
            controller.AddTransition(model.TransitionSource, target);
        model.TransitionSource = NoNode;
        _graphCanvas.CancelInteraction();
        Repaint();
    }

    private void OnCanvasNodeDoubleClicked(string id)
    {
        if (model.Asset is not { } asset || !int.TryParse(id, out var index)) return;
        if (controller.MachineAt(index) is { } machine)
        { controller.Navigate(AnimatorController.ChildPath(model.MachinePath, machine.name)); _graphCanvas?.CancelInteraction(); FrameGraph(); return; }
        if (index < 0 || index >= asset.GetStates(model.LayerIndex, model.MachinePath).Count) return;
        var state = asset.GetStates(model.LayerIndex, model.MachinePath)[index];
        if (state.blendTree is not null)
        {
            model.StateIndex = index;
            model.TransitionIndex = -1;
            model.SelectedStates.Clear();
            model.SelectedStates.Add(index);
            model.InspectorVisible = true;
            Repaint();
        }
        else if (state.clip is { } clip) AnimationWindow.Open(clip);
        _graphCanvas?.CancelInteraction();
    }

    private static bool TryParseConnection(string? id, out int owner, out int index)
    {
        owner = index = -1;
        if (id is null) return false;
        var colon = id.IndexOf(':');
        return colon > 0 && int.TryParse(id.AsSpan(0, colon), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out owner) &&
               int.TryParse(id.AsSpan(colon + 1), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out index);
    }

    private void ProcessGraphDragAndDrop(AnimatorController asset, Rect viewport)
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        var evt = Event.current;
        if (!viewport.Contains(evt.mousePosition) ||
            evt.type is not (EventType.DragUpdated or EventType.DragPerform)) return;
        var clips = DragAndDrop.objectReferences.OfType<AnimationClip>()
            .Where(clip => !clip.isDestroyed).ToArray();
        if (clips.Length == 0)
            clips = DragAndDrop.paths.Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
                .OfType<AnimationClip>().ToArray();
        if (clips.Length == 0) return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (evt.type == EventType.DragPerform)
        {
            var local = evt.mousePosition - new Vector2(viewport.x, viewport.y);
            var node = HitNode(asset, local);
            if (node >= 0 && clips.Length == 1)
                AnimationEditing.Edit(asset, "Assign Animator Motion", () =>
                {
                    var state = asset.GetStates(model.LayerIndex, model.MachinePath)[node];
                    if (state.blendTree is { } tree)
                        tree.motions.Add(new AnimatorBlendMotion { clip = clips[0],
                            threshold = tree.motions.Count == 0 ? Fix64.Zero : tree.motions.Max(item => item.threshold) + Fix64.One });
                    else state.clip = clips[0];
                });
            else
                for (var index = 0; index < clips.Length; index++)
                    controller.AddState(GraphCanvas.ScreenToWorld(local) +
                                        new Vector2(25 * index, 65 * index), clips[index]);
            DragAndDrop.AcceptDrag();
        }
        evt.Use();
    }

    private int HitNode(AnimatorController asset, Vector2 point)
    {
        for (var index = asset.GetStates(model.LayerIndex, model.MachinePath).Count - 1; index >= 0; index--)
            if (NodeRect(asset, index).Contains(point)) return index;
        return NoNode;
    }

    private void ProcessGraphKeyboard(AnimatorController asset)
    {
        var evt = Event.current;
        if (!EditorAssetWritePolicy.CanWrite || !model.GraphFocused || evt.type != EventType.KeyDown ||
            EditorGUIUtility.editingTextField) return;
        var modified = evt.control || evt.command;
        if (evt.keyCode == KeyCode.Escape) model.TransitionSource = NoNode;
        else if (evt.keyCode is KeyCode.Delete or KeyCode.Backspace) controller.DeleteSelection();
        else if (evt.keyCode == KeyCode.F) GraphCanvas.FrameSelection();
        else if (modified && evt.keyCode == KeyCode.A)
        {
            model.SelectedStates.Clear();
            model.SelectedStates.UnionWith(Enumerable.Range(0, asset.GetStates(model.LayerIndex, model.MachinePath).Count));
            model.StateIndex = model.SelectedStates.FirstOrDefault(-1);
            model.TransitionIndex = -1;
        }
        else if (modified && evt.keyCode == KeyCode.C) controller.Copy();
        else if (modified && evt.keyCode == KeyCode.V)
            controller.Paste(GraphCanvas.ScreenToWorld(new Vector2(_graphViewport.width / 2,
                _graphViewport.height / 2)));
        else if (modified && evt.keyCode == KeyCode.D)
        {
            controller.Copy();
            controller.Paste(model.StateIndex >= 0
                ? NodePosition(asset, model.StateIndex) + new Vector2(30, 60)
                : GraphCanvas.ScreenToWorld(new Vector2(_graphViewport.width / 2,
                    _graphViewport.height / 2)));
        }
        else if (modified && evt.keyCode == KeyCode.Z)
        { if (evt.shift) Undo.PerformRedo(); else Undo.PerformUndo(); }
        else if (modified && evt.keyCode == KeyCode.Y) Undo.PerformRedo();
        else return;
        evt.Use();
        Repaint();
    }

    private void EndDrag() => _graphCanvas?.CancelInteraction();

    private void ShowGraphMenu(int node, Vector2 position, string? edgeId)
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        var menu = new GenericMenu();
        model.StateIndex = node;
        model.GraphFocused = true;
        model.SidebarFocused = false;
        if (edgeId is null) model.TransitionIndex = -1;
        if (node >= 0 && !model.SelectedStates.Contains(node))
        { model.SelectedStates.Clear(); model.SelectedStates.Add(node); }
        if (node >= 0) _graphCanvas?.SetSelection(model.SelectedStates.Select(GraphId));
        if (node == NoNode && TryParseConnection(edgeId, out _, out _))
            menu.AddItem(new GUIContent("Delete Transition"), false, controller.DeleteSelection);
        else if (node == NoNode && edgeId is not null)
            menu.AddDisabledItem(new GUIContent("Default Entry Transition"));
        else if (node == NoNode)
        {
            menu.AddItem(new GUIContent("Create Sub-State Machine"), false, () => controller.AddStateMachine(position));
            menu.AddItem(new GUIContent("Create State/Empty"), false, () => controller.AddState(position));
            if (Selection.activeObject is AnimationClip { isDestroyed: false } clip)
                menu.AddItem(new GUIContent("Create State/From Selected Motion"), false,
                    () => controller.AddState(position, clip));
            menu.AddItem(new GUIContent("Paste"), false, () => controller.Paste(position));
            menu.AddItem(new GUIContent("Frame All"), false, FrameGraph);
        }
        else
        {
            if (node != ExitNode)
                menu.AddItem(new GUIContent("Make Transition"), false,
                    () => model.TransitionSource = node);
            if (controller.MachineAt(node) is { } machine)
            {
                menu.AddItem(new GUIContent("Open State Machine"), false, () => controller.Navigate(AnimatorController.ChildPath(model.MachinePath, machine.name)));
                menu.AddItem(new GUIContent("Set as Default State Machine"), false, controller.SetDefault);
                menu.AddItem(new GUIContent("Delete State Machine"), false, controller.DeleteSelection);
            }
            if (node >= 0)
            {
                menu.AddItem(new GUIContent("Set as Default State"), false, controller.SetDefault);
                menu.AddItem(new GUIContent("Copy"), false, controller.Copy);
                menu.AddItem(new GUIContent("Duplicate"), false, () =>
                { controller.Copy(); controller.Paste(position + new Vector2(30, 60)); });
                menu.AddItem(new GUIContent("Frame Selected"), false,
                    () => GraphCanvas.FrameSelection());
                menu.AddItem(new GUIContent("Delete"), false, controller.DeleteSelection);
            }
        }
        menu.ShowAsContext();
    }

    private void FrameGraph()
    {
        _frameRequested = true;
        Repaint();
    }
}
