namespace BEngine.Editor;

[EditorWindowIcon("Icons/AssetAnimation.png")]
public sealed partial class AnimatorWindow : UIElementsEditorWindow<AnimatorWindowModel, AnimatorWindowController>
{
    public AnimatorWindow() { titleContent = new GUIContent("Animator"); minSize = new Vector2(840, 460); }
    protected override AnimatorWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override AnimatorWindowController CreateWindowController(EditorWindowMvcContext context, AnimatorWindowModel model) => new(context, model);
    [MenuItem("Window/Animation/Animator", false, 231)]
    public static void Open() => GetWindow<AnimatorWindow>("Animator").Show();
    public static void Open(AnimatorController asset) { var window = GetWindow<AnimatorWindow>("Animator"); window.SelectAsset(asset); window.Show(); }
    private void SelectAsset(AnimatorController? asset)
    {
        if (ReferenceEquals(model.Asset, asset)) return;
        _frameRequested = true; model.SelectedStates.Clear(); model.Asset = asset; model.LayerIndex = 0; model.MachinePath = ""; model.ParameterIndex = -1;
        model.StateIndex = -1; model.TransitionIndex = -1; model.SidebarFocused = model.GraphFocused = false;
        model.Pan = new Vector2(20, 40); model.Zoom = Fix64.One; model.TransitionSource = NoNode;
    }
    protected override void OnSelectionChange()
    {
        if (model.Locked) return;
        if (Selection.activeObject is AnimatorController asset) SelectAsset(asset);
        else if (Selection.activeGameObject?.GetComponent<Animator>()?.runtimeAnimatorController is { } selected) SelectAsset(selected);
        Repaint();
    }
    protected override void OnEnable() { OnSelectionChange(); EditorApplication.update += UpdateGraph; Undo.undoRedoPerformed += OnHierarchyUndo; }
    protected override void OnDisable() { EditorApplication.update -= UpdateGraph; Undo.undoRedoPerformed -= OnHierarchyUndo; EndDrag(); }
    protected override void OnLostFocus() => EndDrag();
    private void UpdateGraph()
    {
        // Keep the graph in sync with the selected runtime object.  Selection can
        // change from Hierarchy and Inspector without reopening the window.
        if (EditorApplication.isPlaying)
        {
            if (!model.Locked && Selection.activeGameObject?.GetComponent<Animator>()?.runtimeAnimatorController is { } selected &&
                !SameController(selected, model.Asset))
                SelectAsset(selected);
            Repaint();
        }
    }
    protected override void OnGUI()
    {
        var width = GUIUtility.currentViewWidth;
        var compact = width < 650;
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(compact ? 36 : 45))) SelectAsset(new AnimatorController { name = "New Animator Controller" });
        if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(compact ? 40 : 45))) controller.Save();
        var selected = EditorGUILayout.ObjectField("", model.Asset, false,
            GUILayout.Width(Fix64.Max(compact ? 80 : 100, width - (compact ? 350 : 365))));
        if (!ReferenceEquals(selected, model.Asset)) SelectAsset(selected);
        model.SidebarVisible = GUILayout.Toggle(model.SidebarVisible, "Panel", EditorStyles.toolbarButton, GUILayout.Width(compact ? 50 : 52));
        model.InspectorVisible = GUILayout.Toggle(model.InspectorVisible, "Inspector", EditorStyles.toolbarButton, GUILayout.Width(compact ? 72 : 74));
        model.Locked = GUILayout.Toggle(model.Locked, "Lock", EditorStyles.toolbarButton, GUILayout.Width(compact ? 40 : 45));
        if (GUILayout.Button(compact ? "Frame" : "Frame All", EditorStyles.toolbarButton,
                GUILayout.Width(compact ? 50 : 75))) FrameGraph();
        GUILayout.EndHorizontal();
        if (model.Asset is not { isDestroyed: false } asset)
        {
            uiElementContainer.visible = false;
            GUILayout.Label("Select an Animator Controller or create a new one.");
            return;
        }
        model.LayerIndex = Math.Clamp(model.LayerIndex, 0, asset.layerCount - 1);
        controller.ValidateContext();
        DrawBreadcrumbs(asset);
        var height = GUIUtility.currentViewHeight;
        var footerHeight = string.IsNullOrWhiteSpace(model.Message) ? 0 : 20;
        var leftWidth = model.SidebarVisible ? Fix64.Min(220, Fix64.Max(175, width / 4)) : Fix64.Zero;
        var selectedDetail = model.TransitionIndex >= 0 || model.StateIndex != -1;
        var rightWidth = model.InspectorVisible && selectedDetail ? Fix64.Min(285, width / 3) : Fix64.Zero;
        var left = new Rect(0, 52, leftWidth, Fix64.Max(100, height - 52 - footerHeight));
        var graph = new Rect(leftWidth > 0 ? left.xMax + 1 : 0, 52,
            Fix64.Max(80, width - leftWidth - rightWidth - (leftWidth > 0 ? 2 : 0)), left.height);
        var details = new Rect(graph.xMax + 1, 52, rightWidth - 1, left.height);
        if (model.SidebarVisible) DrawSidebar(asset, left);
        DrawGraph(asset, graph);
        if (rightWidth > 0) DrawDetails(asset, details);
        if (footerHeight > 0)
            GUI.Label(new Rect(8, height - 19, width - 16, 18), model.Message, EditorStyles.miniLabel);
    }
    private void DrawSidebar(AnimatorController asset, Rect rect)
    {
        GUI.DrawRect(rect, new Color((Fix64).15, (Fix64).15, (Fix64).16));
        GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 28));
        GUILayout.BeginHorizontal();
        var tabWidth = Fix64.Max(55, (rect.width - 68) / 2);
        if (GUILayout.Toggle(!model.ParametersTab, "Layers", EditorStyles.toolbarButton, GUILayout.Width(tabWidth))) model.ParametersTab = false;
        if (GUILayout.Toggle(model.ParametersTab, rect.width < 195 ? "Params" : "Parameters",
                EditorStyles.toolbarButton, GUILayout.Width(tabWidth))) model.ParametersTab = true;
        if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24)))
        {
            if (model.ParametersTab) ParameterMenu(); else controller.AddLayer();
        }
        GUILayout.EndHorizontal(); GUILayout.EndArea();
        var viewport = new Rect(rect.x + 4, rect.y + 34, rect.width - 8, rect.height - 38);
        var content = model.ParametersTab ? asset.parameters.Count * 34 + 250 : asset.layerCount * 36 + 470 +
            (model.LayerIndex > 0 ? asset.layers[model.LayerIndex - 1].mask.Count * 104 : 0);
        model.SideScroll = GUI.BeginScrollView(viewport, model.SideScroll, new Rect(0, 0, viewport.width - 16, Fix64.Max(viewport.height, content)));
        GUILayout.BeginArea(new Rect(2, 2, viewport.width - 22, Fix64.Max(viewport.height, content)));
        try
        {
            if (model.ParametersTab) DrawParameters(asset);
            else
            {
                for (var i = 0; i < asset.layerCount; i++)
                    if (GUILayout.Button(i == 0 ? "Base Layer" : asset.layers[i - 1].name, i == model.LayerIndex ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton, GUILayout.Height(30)))
                    { model.SidebarFocused = true; model.GraphFocused = false; model.SelectedStates.Clear(); model.LayerIndex = i; model.MachinePath = ""; model.StateIndex = -1; model.TransitionIndex = -1; model.TransitionSource = NoNode; _frameRequested = true; }
                if (model.LayerIndex > 0)
                {
                    var layer = asset.layers[model.LayerIndex - 1];
                    var name = EditorGUILayout.TextField("Name", layer.name);
                    var animator = RuntimeAnimator(asset);
                    var currentWeight = animator?.GetLayerWeight(model.LayerIndex) ?? layer.defaultWeight;
                    var weight = FixedSlider("Weight", currentWeight, 0, 1);
                    if (name != layer.name || animator is null && weight != layer.defaultWeight)
                        AnimationEditing.Edit(asset, "Edit Animator Layer", () => { layer.name = string.IsNullOrWhiteSpace(name) ? layer.name : name; if (animator is null) layer.defaultWeight = weight; });
                    if (animator is not null && weight != currentWeight) animator.SetLayerWeight(model.LayerIndex, weight);
                    DrawLayerOptions(asset, layer, model.LayerIndex);
                    GUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(model.LayerIndex <= 1))
                        if (GUILayout.Button("Up")) controller.MoveLayer(-1);
                    using (new EditorGUI.DisabledScope(model.LayerIndex >= asset.layerCount - 1))
                        if (GUILayout.Button("Down")) controller.MoveLayer(1);
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button("Remove Layer")) controller.RemoveLayer();
                }
            }
        }
        finally { GUILayout.EndArea(); GUI.EndScrollView(); }
        ProcessSidebarKeyboard();
    }
    private void ParameterMenu()
    {
        var menu = new GenericMenu();
        foreach (var type in Enum.GetValues<AnimatorControllerParameterType>())
        { var value = type; menu.AddItem(new GUIContent(type.ToString()), false, () => controller.AddParameter(value)); }
        menu.ShowAsContext();
    }

    private static void DrawLayerOptions(AnimatorController asset, AnimatorControllerLayer layer, int index)
    {
        using var readOnly = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        var blending = (AnimatorLayerBlendingMode)EditorGUILayout.EnumPopup("Blending", layer.blendingMode);
        if (blending != layer.blendingMode) AnimationEditing.Edit(asset, "Set Layer Blending", () => layer.blendingMode = blending);
        if (layer.blendingMode == AnimatorLayerBlendingMode.Additive)
        {
            var reference = EditorGUILayout.ObjectField("Reference Pose", layer.referencePose, false);
            if (reference != layer.referencePose) AnimationEditing.Edit(asset, "Set Additive Reference", () => layer.referencePose = reference);
        }
        var options = new[] { "Independent" }.Concat(Enumerable.Range(0, index).Select(i => i == 0 ? "Base Layer" : asset.layers[i - 1].name)).ToArray();
        var sync = EditorGUILayout.Popup("Sync Layer", layer.syncedLayerIndex + 1, options) - 1;
        if (sync != layer.syncedLayerIndex) AnimationEditing.Edit(asset, "Set Layer Synchronization", () => layer.syncedLayerIndex = sync);
        if (sync >= 0) GUILayout.Label("Matching state paths override source clips.", EditorStyles.miniLabel);
        GUILayout.Label("Transform Mask (empty = all)", EditorStyles.boldLabel);
        foreach (var entry in layer.mask.ToArray())
        {
            var path = EditorGUILayout.TextField("Path", entry.path);
            var children = EditorGUILayout.Toggle("Children", entry.includeChildren);
            var active = EditorGUILayout.Toggle("Include", entry.active);
            if (path != entry.path || children != entry.includeChildren || active != entry.active)
                AnimationEditing.Edit(asset, "Edit Layer Mask", () => { entry.path = path.Trim('/'); entry.includeChildren = children; entry.active = active; });
            if (GUILayout.Button("Remove Mask Path")) AnimationEditing.Edit(asset, "Remove Layer Mask Path", () => layer.mask.Remove(entry));
        }
        if (GUILayout.Button("Add Mask Path")) AnimationEditing.Edit(asset, "Add Layer Mask Path", () => layer.mask.Add(new()));
    }
    private void DrawParameters(AnimatorController asset)
    {
        model.ParameterIndex = Math.Clamp(model.ParameterIndex, -1, asset.parameters.Count - 1);
        for (var index = 0; index < asset.parameters.Count; index++)
        {
            var parameter = asset.parameters[index];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(parameter.name,
                index == model.ParameterIndex ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton,
                GUILayout.Height(28)))
            { model.ParameterIndex = index; model.SidebarFocused = true; model.GraphFocused = false; }
            GUILayout.Label(parameter.type.ToString(), EditorStyles.miniLabel, GUILayout.Width(48));
            GUILayout.EndHorizontal();
        }
        var selected = asset.parameters.ElementAtOrDefault(model.ParameterIndex);
        if (selected is null) return;
        GUILayout.Space(8);
        GUILayout.Label("Parameter", EditorStyles.boldLabel);
        var animator = RuntimeAnimator(asset);
        var name = EditorGUILayout.TextField("Name", selected.name);
        if (name != selected.name) Try(() => AnimationEditing.RenameParameter(asset, selected, name));
        if (selected.type == AnimatorControllerParameterType.Float)
        {
            var value = FixedField(animator is null ? "Default" : "Value", animator?.GetFloat(selected.name) ?? selected.defaultFloat);
            if (animator is not null) animator.SetFloat(selected.name, value);
            else if (value != selected.defaultFloat) AnimationEditing.Edit(asset, "Parameter Default", () => selected.defaultFloat = value);
        }
        else if (selected.type == AnimatorControllerParameterType.Int)
        {
            var value = EditorGUILayout.IntField(animator is null ? "Default" : "Value", animator?.GetInteger(selected.name) ?? selected.defaultInt);
            if (animator is not null) animator.SetInteger(selected.name, value);
            else if (value != selected.defaultInt) AnimationEditing.Edit(asset, "Parameter Default", () => selected.defaultInt = value);
        }
        else if (selected.type == AnimatorControllerParameterType.Bool)
        {
            var value = EditorGUILayout.Toggle(animator is null ? "Default" : "Value", animator?.GetBool(selected.name) ?? selected.defaultBool);
            if (animator is not null) animator.SetBool(selected.name, value);
            else if (value != selected.defaultBool) AnimationEditing.Edit(asset, "Parameter Default", () => selected.defaultBool = value);
        }
        else using (new EditorGUI.DisabledScope(animator is null))
            if (GUILayout.Button("Trigger")) animator?.SetTrigger(selected.name);
        if (GUILayout.Button("Remove Parameter")) controller.RemoveParameter();
    }
    private void ProcessSidebarKeyboard()
    {
        var evt = Event.current;
        if (!model.SidebarFocused || evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
            evt.keyCode is not (KeyCode.Delete or KeyCode.Backspace)) return;
        if (model.ParametersTab && model.ParameterIndex >= 0) controller.RemoveParameter();
        else if (!model.ParametersTab && model.LayerIndex > 0) controller.RemoveLayer();
        else return;
        evt.Use();
    }
    private void DrawDetails(AnimatorController asset, Rect rect)
    {
        using var readOnly = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        GUI.DrawRect(rect, new Color((Fix64).17, (Fix64).17, (Fix64).18));
        var selectedState = asset.GetStates(model.LayerIndex, model.MachinePath).ElementAtOrDefault(model.StateIndex);
        var contentHeight = Fix64.Max(rect.height, SelectedTransition(asset) is { } selectedTransition
            ? 600 + selectedTransition.conditions.Count * 160
            : 420 + (selectedState?.transitions.Count ?? 0) * 28 +
              (selectedState?.blendTree?.EnumerateTrees().Sum(tree => 100 + tree.motions.Count * 210) ?? 0));
        model.DetailScroll = GUI.BeginScrollView(rect, model.DetailScroll, new Rect(0, 0, rect.width - 16, contentHeight));
        GUILayout.BeginArea(new Rect(8, 8, rect.width - 32, contentHeight - 10));
        try
        {
            if (SelectedTransition(asset) is { } transition) DrawTransition(asset, transition);
            else if (asset.GetStates(model.LayerIndex, model.MachinePath).ElementAtOrDefault(model.StateIndex) is { } state) DrawState(asset, state);
            else if (controller.MachineAt(model.StateIndex) is { } machine) DrawMachine(asset, machine);
            else
            {
                GUILayout.Label(model.StateIndex == AnyNode ? "Any State" : model.StateIndex == ExitNode ? "Exit" : model.StateIndex == EntryNode ? "Entry" : "Animator Controller", EditorStyles.boldLabel);
                GUILayout.Label(model.StateIndex == AnyNode ? "Transitions can interrupt any state." : model.StateIndex == ExitNode ? "Exit follows the parent graph's outgoing transitions; root Exit ends the layer." : "Select a state or transition to edit.");
                GUILayout.Space(12);
                if (GUILayout.Button("Create State")) controller.AddState();
            }
        }
        finally { GUILayout.EndArea(); GUI.EndScrollView(); }
    }
    private void DrawState(AnimatorController asset, AnimatorState state)
    {
        GUILayout.Label(state.name, EditorStyles.boldLabel);
        var name = EditorGUILayout.TextField("Name", state.name);
        if (name != state.name) Try(() => AnimationEditing.RenameState(asset, state, name));
        EditorGUI.BeginChangeCheck();
        var motionType = EditorGUILayout.Popup("Motion", state.blendTree is null ? 0 : 1,
            ["Animation Clip", "Blend Tree"]);
        if (EditorGUI.EndChangeCheck())
            AnimationEditing.Edit(asset, "Change Animator Motion", () => state.blendTree = motionType == 0
                ? null : new AnimatorBlendTree
                {
                    parameter = asset.parameters.FirstOrDefault(item => item.type is
                        AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int)?.name ?? string.Empty,
                    motions = state.clip is { } original ? [new AnimatorBlendMotion { clip = original }] : []
                });
        if (state.blendTree is { } tree) DrawBlendTree(asset, tree);
        else
        {
            var clip = EditorGUILayout.ObjectField("Clip", state.clip, false);
            if (clip != state.clip)
                AnimationEditing.Edit(asset, "Assign Animator Clip", () => state.clip = clip);
            if (clip is not null && GUILayout.Button("Edit Motion")) AnimationWindow.Open(clip);
        }
        var speed = FixedField("Speed", state.speed);
        var loop = EditorGUILayout.Toggle("Loop", state.loop);
        if (speed != state.speed || loop != state.loop)
            AnimationEditing.Edit(asset, "Edit Animator State", () => { state.speed = speed; state.loop = loop; });
        if (GUILayout.Button("Set as Default State")) controller.SetDefault();
        if (GUILayout.Button("Make Transition")) model.TransitionSource = model.StateIndex;
        GUILayout.Label("Transitions", EditorStyles.boldLabel);
        for (var index = 0; index < state.transitions.Count; index++)
            if (GUILayout.Button(state.name + " -> " + DestinationName(state.transitions[index]))) { model.TransitionOwner = model.StateIndex; model.TransitionIndex = index; }
    }

    private void DrawBlendTree(AnimatorController asset, AnimatorBlendTree tree, int depth = 0)
    {
        if (depth > 16) { GUILayout.Label("Blend tree nesting exceeds 16 levels."); return; }
        var mode = (AnimatorBlendTreeType)EditorGUILayout.EnumPopup("Blend Type", tree.blendType);
        if (mode != tree.blendType) AnimationEditing.Edit(asset, "Set Blend Type", () => tree.blendType = mode);
        var parameters = asset.parameters.Where(item => item.type is AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int)
            .Select(item => item.name).ToArray();
        if (parameters.Length == 0) GUILayout.Label("Add a Float or Int parameter to drive this blend.");
        else
        {
            Parameter("Parameter X", tree.parameter, value => tree.parameter = value);
            if (tree.blendType == AnimatorBlendTreeType.Cartesian2D)
                Parameter("Parameter Y", tree.parameterY, value => tree.parameterY = value);
        }
        void Parameter(string label, string current, Action<string> assign)
        {
            var options = new[] { "None" }.Concat(parameters).ToArray();
            EditorGUI.BeginChangeCheck();
            var index = EditorGUILayout.Popup(label, Math.Max(0, Array.IndexOf(parameters, current) + 1), options);
            if (EditorGUI.EndChangeCheck()) AnimationEditing.Edit(asset, "Set Blend Parameter", () => assign(index == 0 ? string.Empty : parameters[index - 1]));
        }
        for (var index = 0; index < tree.motions.Count; index++)
        {
            var motion = tree.motions[index];
            GUILayout.Label("Motion " + (index + 1), EditorStyles.boldLabel);
            bool nested;
            using (new EditorGUI.DisabledScope(depth >= 16 && motion.blendTree is null))
                nested = EditorGUILayout.Toggle("Nested Blend", motion.blendTree is not null);
            if (nested != (motion.blendTree is not null))
                AnimationEditing.Edit(asset, "Change Blend Motion Type", () => motion.blendTree = nested ? new AnimatorBlendTree { parameter = tree.parameter } : null);
            if (tree.blendType == AnimatorBlendTreeType.Cartesian2D)
            {
                var x = FixedField("Position X", motion.position.x); var y = FixedField("Position Y", motion.position.y);
                if (new Vector2(x, y) != motion.position) AnimationEditing.Edit(asset, "Set Blend Position", () => motion.position = new(x, y));
            }
            else
            {
                var threshold = FixedField("Threshold", motion.threshold);
                if (threshold != motion.threshold) AnimationEditing.Edit(asset, "Set Blend Threshold", () => motion.threshold = threshold);
            }
            if (motion.blendTree is { } child)
            {
                GUILayout.Label("Nested Tree " + (depth + 1), EditorStyles.boldLabel);
                DrawBlendTree(asset, child, depth + 1);
            }
            else
            {
                var clip = EditorGUILayout.ObjectField("Clip", motion.clip, false);
                if (clip != motion.clip) AnimationEditing.Edit(asset, "Set Blend Clip", () => motion.clip = clip);
                using (new EditorGUI.DisabledScope(clip is null))
                    if (GUILayout.Button("Edit Clip")) AnimationWindow.Open(clip!);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove"))
            {
                AnimationEditing.Edit(asset, "Remove Blend Motion", () => tree.motions.Remove(motion));
                GUILayout.EndHorizontal();
                break;
            }
            GUILayout.EndHorizontal();
        }
        using (new EditorGUI.DisabledScope(tree.motions.Count >= 128))
            if (GUILayout.Button("Add Motion"))
                AnimationEditing.Edit(asset, "Add Blend Motion", () => tree.motions.Add(new AnimatorBlendMotion
                { threshold = tree.motions.Count == 0 ? Fix64.Zero : tree.motions.Max(item => item.threshold) + Fix64.One }));
    }
    private void DrawTransition(AnimatorController asset, AnimatorTransition transition)
    {
        var sourceName = model.TransitionOwner == AnyNode ? "Any State"
            : model.TransitionOwner == EntryNode ? "Entry"
            : controller.NodeName(model.TransitionOwner) ?? "State";
        GUILayout.Label(sourceName + " -> " + DestinationName(transition), EditorStyles.boldLabel);
        var transitionList = controller.NodeTransitions(model.TransitionOwner);
        if (transitionList is not null)
        {
            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(model.TransitionIndex <= 0)) if (GUILayout.Button("Move Up")) MoveTransition(-1);
            using (new EditorGUI.DisabledScope(model.TransitionIndex >= transitionList.Count - 1)) if (GUILayout.Button("Move Down")) MoveTransition(1);
            GUILayout.EndHorizontal();
            void MoveTransition(int delta)
            {
                var index = model.TransitionIndex; var target = index + delta;
                if (index < 0 || target < 0 || index >= transitionList.Count || target >= transitionList.Count) return;
                AnimationEditing.Edit(asset, "Reorder Animator Transition", () => (transitionList[index], transitionList[target]) = (transitionList[target], transitionList[index]));
                model.TransitionIndex = target;
            }
        }
        var destinations = asset.GetMachinePaths(model.LayerIndex).SelectMany(path =>
            asset.GetStates(model.LayerIndex, path).Select(state => AnimatorController.ChildPath(path, state.name))
            .Concat(asset.GetStateMachines(model.LayerIndex, path).Select(machine => AnimatorController.ChildPath(path, machine.name)))).ToArray();
        var names = destinations.Select(path => "/" + path).Append("Exit").ToArray();
        var resolved = asset.ResolveDestinationPath(model.LayerIndex, model.MachinePath, transition.destinationState);
        var current = transition.isExit ? names.Length - 1 : Array.IndexOf(destinations, resolved);
        if (current < 0) { names = names.Prepend("Missing: " + transition.destinationState).ToArray(); current = 0; }
        EditorGUI.BeginChangeCheck();
        var destination = EditorGUILayout.Popup("Destination", current, names);
        var changedDestination = EditorGUI.EndChangeCheck();
        var exit = EditorGUILayout.Toggle("Has Exit Time", transition.hasExitTime);
        var exitTime = FixedField("Exit Time", transition.exitTime);
        var fixedDuration = EditorGUILayout.Toggle("Fixed Duration", transition.hasFixedDuration);
        var duration = FixedField(fixedDuration ? "Duration (seconds)" : "Duration (normalized)", transition.duration);
        var offset = FixedField("Transition Offset", transition.offset);
        var self = EditorGUILayout.Toggle("Can Transition To Self", transition.canTransitionToSelf);
        if (changedDestination || exit != transition.hasExitTime || (Fix64)exitTime != transition.exitTime || fixedDuration != transition.hasFixedDuration || (Fix64)duration != transition.duration || (Fix64)offset != transition.offset || self != transition.canTransitionToSelf)
            AnimationEditing.Edit(asset, "Edit Transition", () =>
            {
                if (changedDestination) { transition.isExit = destination == names.Length - 1; transition.destinationState = transition.isExit ? "" : names[destination]; }
                transition.hasExitTime = exit; transition.exitTime = Fix64.Max(0, (Fix64)exitTime);
                transition.hasFixedDuration = fixedDuration; transition.duration = Fix64.Max(0, (Fix64)duration); transition.offset = Fix64.Max(0, (Fix64)offset); transition.canTransitionToSelf = self;
            });
        GUILayout.Label("Conditions", EditorStyles.boldLabel);
        foreach (var condition in transition.conditions.ToArray())
        {
            var parameters = asset.parameters.Select(p => p.name).ToArray();
            if (parameters.Length == 0) { GUILayout.Label("Create a parameter to add a condition."); break; }
            var parameterIndex = EditorGUILayout.Popup("Parameter", Math.Max(0, Array.IndexOf(parameters, condition.parameter)), parameters);
            var parameter = asset.parameters[parameterIndex];
            var modes = parameter.type is AnimatorControllerParameterType.Bool or AnimatorControllerParameterType.Trigger
                ? new[] { AnimatorConditionMode.If, AnimatorConditionMode.IfNot }
                : parameter.type == AnimatorControllerParameterType.Float ? [AnimatorConditionMode.Greater, AnimatorConditionMode.Less]
                : new[] { AnimatorConditionMode.Greater, AnimatorConditionMode.Less, AnimatorConditionMode.Equals, AnimatorConditionMode.NotEqual };
            var modeIndex = EditorGUILayout.Popup("Condition", Math.Max(0, Array.IndexOf(modes, condition.mode)), modes.Select(mode => mode.ToString()).ToArray());
            var threshold = condition.threshold;
            if (parameter.type is AnimatorControllerParameterType.Float or AnimatorControllerParameterType.Int)
            {
                if (parameter.type == AnimatorControllerParameterType.Float) threshold = FixedField("Threshold", threshold);
                else
                {
                    EditorGUI.BeginChangeCheck(); var integer = EditorGUILayout.IntField("Threshold", (int)threshold);
                    if (EditorGUI.EndChangeCheck()) threshold = integer;
                }
            }
            if (parameters[parameterIndex] != condition.parameter || modes[modeIndex] != condition.mode || threshold != condition.threshold)
                AnimationEditing.Edit(asset, "Edit Condition", () => { condition.parameter = parameters[parameterIndex]; condition.mode = modes[modeIndex]; condition.threshold = threshold; });
            if (GUILayout.Button("Remove Condition")) AnimationEditing.Edit(asset, "Remove Condition", () => transition.conditions.Remove(condition));
        }
        using (new EditorGUI.DisabledScope(asset.parameters.Count == 0))
            if (GUILayout.Button("+ Add Condition")) AnimationEditing.Edit(asset, "Add Condition", () => transition.conditions.Add(new AnimatorCondition { parameter = asset.parameters[0].name,
                mode = asset.parameters[0].type is AnimatorControllerParameterType.Bool or AnimatorControllerParameterType.Trigger ? AnimatorConditionMode.If : AnimatorConditionMode.Greater }));
        if (GUILayout.Button("Delete Transition")) controller.DeleteSelection();
    }
    private static string DestinationName(AnimatorTransition transition) => transition.isExit ? "Exit" : transition.destinationState;
    private AnimatorTransition? SelectedTransition(AnimatorController asset) => model.TransitionIndex < 0 ? null :
        controller.NodeTransitions(model.TransitionOwner)?.ElementAtOrDefault(model.TransitionIndex);
    private static Animator? RuntimeAnimator(AnimatorController asset) =>
        EditorApplication.isPlaying && Selection.activeGameObject?.GetComponent<Animator>() is { } animator &&
        SameController(animator.runtimeAnimatorController, asset) ? animator : null;

    private static bool SameController(AnimatorController? left, AnimatorController? right)
    {
        if (left is null || right is null || left.isDestroyed || right.isDestroyed) return false;
        if (ReferenceEquals(left, right)) return true;
        if (!string.IsNullOrWhiteSpace(left.guid) &&
            left.guid.Equals(right.guid, StringComparison.OrdinalIgnoreCase)) return true;
        return !string.IsNullOrWhiteSpace(left.assetPath) &&
               left.assetPath.Equals(right.assetPath, StringComparison.OrdinalIgnoreCase);
    }
    private static Fix64 FixedField(string label, Fix64 value)
    {
        EditorGUI.BeginChangeCheck();
        var result = EditorGUILayout.FloatField(label, (float)value);
        return EditorGUI.EndChangeCheck() ? (Fix64)result : value;
    }
    private static Fix64 FixedSlider(string label, Fix64 value, float minimum, float maximum)
    {
        EditorGUI.BeginChangeCheck();
        var result = EditorGUILayout.Slider(label, (float)value, minimum, maximum);
        return EditorGUI.EndChangeCheck() ? (Fix64)result : value;
    }
    private void Try(Action action) { try { action(); model.Message = ""; } catch (ArgumentException exception) { model.Message = exception.Message; } }
}

public sealed class AnimatorWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public AnimatorController? Asset { get; internal set; }
    public string MachinePath { get; internal set; } = "";
    internal string NewName = "New State", Message = "";
    internal int StateIndex = -1, LayerIndex, ParameterIndex = -1, TransitionOwner, TransitionIndex = -1, TransitionSource = -100;
    internal bool ParametersTab, Locked, GraphFocused, SidebarFocused, SidebarVisible = true, InspectorVisible = true;
    internal readonly HashSet<int> SelectedStates = [];
    internal Vector2 SideScroll, DetailScroll, Pan = new(20, 40);
    internal Fix64 Zoom = Fix64.One;
}

public sealed partial class AnimatorWindowController(EditorWindowMvcContext context, AnimatorWindowModel model)
    : EditorWindowController<AnimatorWindow, AnimatorWindowModel>(context, model)
{
    private AnimatorState[] _clipboard = [];
    public void AddState() => AddState(new Vector2(230, 100 + (Model.Asset?.GetStates(Model.LayerIndex, Model.MachinePath).Count ?? 0) * 90));
    public void AddState(Vector2 position, AnimationClip? motion = null)
    {
        if (Model.Asset is not { } asset) return;
        var states = asset.GetStates(Model.LayerIndex, Model.MachinePath);
        var name = (motion?.name ?? Model.NewName).Trim(); if (name.Length == 0) name = "New State";
        name = name.Replace('/', '_'); if (name is "." or "..") name = "New State";
        var original = name; var suffix = 1; while (states.Any(state => state.name == name) || asset.GetStateMachines(Model.LayerIndex, Model.MachinePath).Any(machine => machine.name == name)) name = original + " " + suffix++;
        AnimationEditing.Edit(asset, "Add Animator State", () => { states.Add(new AnimatorState { name = name, clip = motion, editorPosition = position }); if (string.IsNullOrWhiteSpace(asset.GetDefaultState(Model.LayerIndex, Model.MachinePath))) asset.SetDefaultState(Model.LayerIndex, Model.MachinePath, name); });
        Model.SelectedStates.Clear(); Model.StateIndex = states.Count - 1; Model.SelectedStates.Add(Model.StateIndex); Model.TransitionIndex = -1;
    }
    public void AddLayer()
    {
        if (Model.Asset is not { } asset) return;
        var name = "New Layer"; var suffix = 1; while (asset.layers.Any(layer => layer.name == name)) name = "New Layer " + suffix++;
        AnimationEditing.Edit(asset, "Add Animator Layer", () => asset.layers.Add(new AnimatorControllerLayer { name = name }));
        Model.LayerIndex = asset.layers.Count; Model.MachinePath = ""; Model.StateIndex = -1; Model.TransitionIndex = -1;
    }
    public void MoveLayer(int direction)
    {
        if (Model.Asset is not { } asset || Model.LayerIndex <= 0) return;
        var index = Model.LayerIndex - 1;
        var target = index + direction;
        if (target < 0 || target >= asset.layers.Count) return;
        int Map(int layer) => layer == index + 1 ? target + 1 : layer == target + 1 ? index + 1 : layer;
        for (var i = 0; i < asset.layers.Count; i++)
            if (asset.layers[i].syncedLayerIndex >= 0 && Map(asset.layers[i].syncedLayerIndex) >= Map(i + 1))
            { Model.Message = "Keep synchronized layers after their source layer."; return; }
        AnimationEditing.Edit(asset, "Reorder Animator Layers", () =>
        {
            foreach (var layer in asset.layers) if (layer.syncedLayerIndex >= 0) layer.syncedLayerIndex = Map(layer.syncedLayerIndex);
            (asset.layers[index], asset.layers[target]) = (asset.layers[target], asset.layers[index]);
        });
        Model.LayerIndex = target + 1;
    }
    public void RemoveLayer()
    {
        if (Model.Asset is not { } asset || Model.LayerIndex <= 0 || Model.LayerIndex > asset.layers.Count) return;
        var index = Model.LayerIndex - 1;
        AnimationEditing.Edit(asset, "Remove Animator Layer", () =>
        {
            asset.layers.RemoveAt(index);
            foreach (var layer in asset.layers)
                if (layer.syncedLayerIndex == index + 1) layer.syncedLayerIndex = -1;
                else if (layer.syncedLayerIndex > index + 1) layer.syncedLayerIndex--;
        });
        Model.LayerIndex = Math.Min(Model.LayerIndex, asset.layerCount - 1); Model.MachinePath = "";
        Model.SelectedStates.Clear(); Model.StateIndex = -1; Model.TransitionIndex = -1;
    }
    public void AddParameter(AnimatorControllerParameterType type)
    {
        if (Model.Asset is not { } asset) return;
        var name = "New " + type; var suffix = 1; while (asset.parameters.Any(parameter => parameter.name == name)) name = "New " + type + " " + suffix++;
        AnimationEditing.Edit(asset, "Add Animator Parameter", () => asset.parameters.Add(new AnimatorControllerParameter { name = name, type = type }));
        Model.ParameterIndex = asset.parameters.Count - 1;
    }
    public void RemoveParameter()
    {
        if (Model.Asset is not { } asset || Model.ParameterIndex < 0 || Model.ParameterIndex >= asset.parameters.Count) return;
        var parameter = asset.parameters[Model.ParameterIndex];
        AnimationEditing.Edit(asset, "Remove Animator Parameter", () =>
        {
            asset.parameters.Remove(parameter);
            foreach (var transition in asset.GetAllTransitions())
                transition.conditions.RemoveAll(condition => condition.parameter == parameter.name);
            foreach (var state in Enumerable.Range(0, asset.layerCount).SelectMany(asset.GetAllStates))
                foreach (var tree in state.blendTree?.EnumerateTrees() ?? [])
                {
                    if (tree.parameter == parameter.name) tree.parameter = string.Empty;
                    if (tree.parameterY == parameter.name) tree.parameterY = string.Empty;
                }
        });
        Model.ParameterIndex = Math.Min(Model.ParameterIndex, asset.parameters.Count - 1);
    }
    public void SetDefault()
    {
        if (Model.Asset is { } asset && NodeName(Model.StateIndex) is { } name)
            AnimationEditing.Edit(asset, "Set Default State", () => asset.SetDefaultState(Model.LayerIndex, Model.MachinePath, name));
    }
    public void AddTransition(int source, int destination)
    {
        if (Model.Asset is not { } asset || destination != -4 && NodeName(destination) is null) return;
        var list = NodeTransitions(source);
        if (list is null) return;
        AnimationEditing.Edit(asset, "Add Animator Transition", () => list.Add(new AnimatorTransition { isExit = destination == -4, destinationState = destination == -4 ? "" : NodeName(destination)!,
            hasExitTime = source >= 0, canTransitionToSelf = source != -2 }));
        Model.TransitionOwner = source; Model.TransitionIndex = list.Count - 1;
    }
    public void DeleteSelection()
    {
        if (Model.Asset is not { } asset) return;
        var states = asset.GetStates(Model.LayerIndex, Model.MachinePath);
        if (Model.TransitionIndex >= 0)
        {
            var list = NodeTransitions(Model.TransitionOwner);
            if (list is not null && Model.TransitionIndex < list.Count) { var index = Model.TransitionIndex; AnimationEditing.Edit(asset, "Delete Transition", () => list.RemoveAt(index)); }
        }
        else if (MachineAt(Model.StateIndex) is { } machine)
            AnimationEditing.RemoveStateMachine(asset, Model.LayerIndex, AnimatorController.ChildPath(Model.MachinePath, machine.name));
        else
        {
            var selected = (Model.SelectedStates.Count > 0 ? Model.SelectedStates : [Model.StateIndex]).Where(index => index >= 0 && index < states.Count).Select(index => states[index]).ToArray();
            if (selected.Length > 0) AnimationEditing.Edit(asset, "Delete Animator States", () =>
            { foreach (var state in selected) AnimationEditing.RemoveState(asset, state); });
        }
        Model.SelectedStates.Clear(); Model.StateIndex = -1; Model.TransitionIndex = -1;
    }
    public void Copy()
    {
        if (Model.Asset is not { } asset) return;
        var states = asset.GetStates(Model.LayerIndex, Model.MachinePath);
        _clipboard = (Model.SelectedStates.Count > 0 ? Model.SelectedStates : [Model.StateIndex]).Where(index => index >= 0 && index < states.Count).Order().Select(index => CloneState(states[index])).ToArray();
        var copiedPaths = _clipboard.ToDictionary(state => AnimatorController.ChildPath(Model.MachinePath, state.name), state => state.name, StringComparer.Ordinal);
        foreach (var state in _clipboard)
            foreach (var transition in state.transitions)
                if (!transition.isExit && asset.ResolveDestinationPath(Model.LayerIndex, Model.MachinePath, transition.destinationState) is { } target)
                    transition.destinationState = copiedPaths.TryGetValue(target, out var local) ? local : "/" + target;
    }
    public void Paste(Vector2 position)
    {
        if (_clipboard.Length == 0 || Model.Asset is not { } asset) return;
        var states = asset.GetStates(Model.LayerIndex, Model.MachinePath); var copies = _clipboard.Select(CloneState).ToArray();
        var names = states.Select(state => state.name).Concat(asset.GetStateMachines(Model.LayerIndex, Model.MachinePath).Select(machine => machine.name)).ToHashSet(StringComparer.Ordinal); var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        var origin = _clipboard[0].editorPosition ?? Vector2.zero;
        foreach (var copy in copies)
        {
            var original = copy.name; var suffix = 1; while (!names.Add(copy.name)) copy.name = original + " " + suffix++;
            renamed[original] = copy.name; copy.editorPosition = position + (copy.editorPosition ?? origin) - origin;
        }
        foreach (var copy in copies)
        {
            foreach (var transition in copy.transitions) if (!transition.isExit && renamed.TryGetValue(transition.destinationState, out var destination)) transition.destinationState = destination;
            copy.transitions.RemoveAll(transition => !transition.isExit && !names.Contains(transition.destinationState) && asset.ResolveDestinationPath(Model.LayerIndex, Model.MachinePath, transition.destinationState) is null);
        }
        var first = states.Count;
        AnimationEditing.Edit(asset, "Paste Animator States", () => { states.AddRange(copies); if (string.IsNullOrWhiteSpace(asset.GetDefaultState(Model.LayerIndex, Model.MachinePath))) asset.SetDefaultState(Model.LayerIndex, Model.MachinePath, copies[0].name); });
        Model.SelectedStates.Clear(); foreach (var index in Enumerable.Range(first, copies.Length)) Model.SelectedStates.Add(index);
        Model.StateIndex = states.Count - 1; Model.TransitionIndex = -1;
    }
    private static AnimatorState CloneState(AnimatorState state) => new()
    {
        name = state.name, clip = state.clip,
        blendTree = state.blendTree?.Clone(),
        speed = state.speed, loop = state.loop, editorPosition = state.editorPosition,
        transitions = [.. state.transitions.Select(transition => new AnimatorTransition { destinationState = transition.destinationState, isExit = transition.isExit,
            hasExitTime = transition.hasExitTime, exitTime = transition.exitTime, duration = transition.duration, hasFixedDuration = transition.hasFixedDuration,
            offset = transition.offset, canTransitionToSelf = transition.canTransitionToSelf,
            conditions = [.. transition.conditions.Select(condition => new AnimatorCondition { parameter = condition.parameter, mode = condition.mode, threshold = condition.threshold })] })]
    };
    public void Save()
    {
        try { if (Model.Asset is { } asset) Model.Message = "Saved " + AnimationEditing.Save(asset); }
        catch (Exception exception) { Model.Message = exception.Message; }
    }
}
