using BEngine.Editor;
using BEngine.Editor.UIElements;

namespace BEngine.VisualScripting.Editor;

[EditorWindowIcon("Icons/Assets/AssetScript.png")]
public sealed class VisualScriptWindow : UIElementsEditorWindow<VisualScriptWindowModel, VisualScriptWindowController>
{
    private NodeCanvas? _canvas;
    private readonly EditorScrollView _scrollView = new();
    private Vector2 _scroll;

    public VisualScriptWindow()
    {
        titleContent = new("Visual Script");
        minSize = new(760, 430);
    }

    protected override VisualScriptWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override VisualScriptWindowController CreateWindowController(EditorWindowMvcContext context,
        VisualScriptWindowModel model) => new(context, model);

    [MenuItem("Window/Scripting/Visual Script", false, 210)]
    public static void Open() => GetWindow<VisualScriptWindow>("Visual Script").Show();

    public static void Open(VisualScriptGraph graph)
    {
        var window = GetWindow<VisualScriptWindow>("Visual Script");
        window.controller.Select(graph);
        window.Show();
    }

    [OnOpenAsset]
    private static bool OpenAsset(string path)
    {
        if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(VisualScriptGraph)) return false;
        if (AssetDatabase.LoadAssetAtPath<VisualScriptGraph>(path) is not { } graph) return false;
        Open(graph);
        return true;
    }

    protected override void OnEnable()
    {
        OnSelectionChange();
        Undo.undoRedoPerformed += Repaint;
        VisualScriptDebugHub.stateChanged += OnDebugStateChanged;
    }

    protected override void OnDisable()
    {
        _canvas?.CancelInteraction();
        Undo.undoRedoPerformed -= Repaint;
        VisualScriptDebugHub.stateChanged -= OnDebugStateChanged;
    }

    protected override void OnLostFocus() => _canvas?.CancelInteraction();
    protected override void Update() { if (EditorApplication.isPlaying) Repaint(); }

    protected override void OnSelectionChange()
    {
        if (Selection.activeObject is VisualScriptGraph graph) controller.Select(graph);
        Repaint();
    }

    private void OnDebugStateChanged(VisualScriptDebugSnapshot _) => Repaint();

    private NodeCanvas Canvas
    {
        get
        {
            if (_canvas is not null) return _canvas;
            var canvas = new NodeCanvas();
            canvas.SelectionChanged += (nodes, edge) =>
            {
                model.Selected = nodes.ToHashSet(StringComparer.Ordinal);
                model.SelectedConnection = edge;
                Repaint();
            };
            canvas.NodeDragStarted += () => controller.BeginNodeMove();
            canvas.NodesMoved += controller.MoveNodes;
            canvas.PortConnectionRequested += controller.Connect;
            canvas.ContextRequested += (_, _, position) => ShowCreateMenu(position);
            canvas.ViewChanged += (_, _) => Repaint();
            rootVisualElement.Add(canvas);
            return _canvas = canvas;
        }
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        if (model.Asset is not { isDestroyed: false } graph)
        {
            uiElementContainer.visible = false;
            EditorGUILayout.HelpBox("Create or select a Visual Script graph.");
            return;
        }

        var width = GUIUtility.currentViewWidth;
        var height = GUIUtility.currentViewHeight;
        var viewport = new Rect(0, 30, Fix64.Max(1, width - 300), Fix64.Max(1, height - 58));
        uiElementContainer.position = viewport;
        uiElementContainer.visible = true;
        var canvas = Canvas;
        canvas.allowEditing = EditorAssetWritePolicy.CanWrite;
        canvas.SetViewport(viewport.size);
        var runner = SelectedRunner();
        var flow = new Color32(173, 202, 225, 255);
        canvas.SetGraph(graph.nodes.Select(node => new NodeCanvasNode(node.id, Title(node.kind), node.position,
            new(210, Math.Max(70, 50 + Math.Max(Inputs(node.kind).Count, VisualScriptGraph.OutputPorts(node.kind).Count) * 22)),
            NodeColor(node), Detail(node), runner?.currentNodeId == node.id, runner?.currentNodeId == node.id ? Fix64.One : Fix64.Zero)
        {
            Inputs = Inputs(node.kind).Select(port => new NodeCanvasPort(port, port, flow)).ToArray(),
            Outputs = VisualScriptGraph.OutputPorts(node.kind).Select(port => new NodeCanvasPort(port, port, flow)).ToArray()
        }), graph.connections.Select(edge => new NodeCanvasConnection(edge.id, edge.source, edge.destination, flow,
            SourcePort: edge.sourcePort, DestinationPort: edge.destinationPort)));
        canvas.SetSelection(model.Selected, model.SelectedConnection);
        if (model.Frame) { canvas.FrameAll(); model.Frame = false; }
        canvas.SetCaption("Flow ports | drag to connect | middle mouse to pan | wheel to zoom");

        using (GUILayout.Area(new Rect(width - 294, 34, 288, Fix64.Max(1, height - 64))))
        {
            _scroll = _scrollView.Begin(_scroll);
            try
            {
                DrawInspector(graph);
                DrawDebugger(graph);
            }
            finally { _scrollView.End(); }
        }
        GUI.Label(new Rect(6, height - 26, width - 12, 22), model.Message, EditorStyles.miniLabel);

        if (canvas.hasFocus && Event.current is { type: EventType.KeyDown } evt)
        {
            if (evt.keyCode == KeyCode.Delete && EditorAssetWritePolicy.CanWrite)
            { controller.DeleteSelection(); evt.Use(); }
            else if (evt.keyCode == KeyCode.F) { model.Frame = true; evt.Use(); }
        }
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(45)))
                controller.Select(VisualScriptGraph.CreateDefault());
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45))) controller.Save();
            if (GUILayout.Button("Add", EditorStyles.toolbarButton, GUILayout.Width(42)))
                ShowCreateMenu(Canvas.ScreenToWorld(Canvas.viewport / 2));
            if (GUILayout.Button("Delete", EditorStyles.toolbarButton, GUILayout.Width(55))) controller.DeleteSelection();
        }
        if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(65))) controller.Validate();
        if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(50))) model.Frame = true;
        var selected = EditorGUILayout.ObjectField("", model.Asset, false,
            GUILayout.Width(Fix64.Max(100, GUIUtility.currentViewWidth - 330)));
        if (!ReferenceEquals(selected, model.Asset)) controller.Select(selected);
        GUILayout.EndHorizontal();
    }

    private void DrawInspector(VisualScriptGraph graph)
    {
        GUILayout.Label("Graph", EditorStyles.boldLabel);
        var node = graph.nodes.FirstOrDefault(item => model.Selected.Contains(item.id));
        if (node is null)
        {
            using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            {
                EditorGUI.BeginChangeCheck();
                var initial = EditorGUILayout.TextField("Initial State", graph.initialState);
                if (EditorGUI.EndChangeCheck()) controller.Edit("Change Initial State", () => graph.initialState = initial.Trim());
            }
            DrawVariables(graph);
            return;
        }
        GUILayout.Label(Title(node.kind));
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            EditorGUI.BeginChangeCheck();
            var breakpoint = EditorGUILayout.Toggle("Breakpoint", node.breakpoint);
            var variable = node.variable;
            var secondary = node.secondaryVariable;
            var text = node.text;
            var value = node.value.Clone();
            var comparison = node.comparison;
            var relative = node.relative;
            var graphPath = node.graphPath;
            var actionId = node.actionId;
            if (VisualScriptGraph.RequiresVariable(node.kind)) variable = VariablePopup("Variable", variable, graph.variables);
            if (node.kind == VisualScriptNodeKind.CompareNumber)
            {
                comparison = (VisualScriptComparison)EditorGUILayout.EnumPopup("Comparison", comparison);
                secondary = VariablePopup("Result", secondary,
                    graph.variables.Where(item => item.value.kind == VisualScriptValueKind.Boolean).ToList());
                value.number = (Fix64)EditorGUILayout.FloatField("Right value", (float)value.number);
            }
            if (node.kind is VisualScriptNodeKind.SetVariable or VisualScriptNodeKind.AddNumber or VisualScriptNodeKind.MultiplyNumber)
            {
                var kind = graph.variables.FirstOrDefault(item => item.name == variable)?.value.kind ?? value.kind;
                value.kind = kind;
                DrawValue(value, kind, node.kind == VisualScriptNodeKind.AddNumber ? "Amount" : "Value");
            }
            if (node.kind == VisualScriptNodeKind.SetPosition)
            {
                value.kind = VisualScriptValueKind.Vector2;
                value.vector = EditorGUILayout.Vector2Field("Position", value.vector);
                relative = EditorGUILayout.Toggle("Relative", relative);
            }
            if (node.kind is VisualScriptNodeKind.CustomEvent or VisualScriptNodeKind.Log)
                text = EditorGUILayout.TextField(node.kind == VisualScriptNodeKind.CustomEvent ? "Event" : "Message", text);
            if (node.kind is VisualScriptNodeKind.StateEnterEvent or VisualScriptNodeKind.StateUpdateEvent or VisualScriptNodeKind.StateExitEvent or VisualScriptNodeKind.TransitionState)
                text = EditorGUILayout.TextField("State", text);
            if (node.kind == VisualScriptNodeKind.CallSubGraph)
            {
                graphPath = EditorGUILayout.TextField("Graph Path / GUID", graphPath);
                text = EditorGUILayout.TextField("Event (empty: Start)", text);
            }
            if (node.kind == VisualScriptNodeKind.CustomAction)
            {
                var ids = VisualScriptActions.RegisteredIds;
                if (ids.Count > 0) actionId = ids[Math.Clamp(EditorGUILayout.Popup("Action", Math.Max(0, ids.ToList().IndexOf(actionId)), ids.ToArray()), 0, ids.Count - 1)];
                else actionId = EditorGUILayout.TextField("Action ID", actionId);
            }
            if (node.kind == VisualScriptNodeKind.Wait)
            { value.kind = VisualScriptValueKind.Number; value.number = Fix64.Max(0, (Fix64)EditorGUILayout.FloatField("Seconds", (float)value.number)); }
            if (node.kind == VisualScriptNodeKind.KeyPressed)
            { var key = Enum.TryParse<KeyCode>(text, out var current) ? current : KeyCode.Space; text = ((KeyCode)EditorGUILayout.EnumPopup("Key", key)).ToString(); }
            if (node.kind == VisualScriptNodeKind.SetActive)
            { value.kind = VisualScriptValueKind.Boolean; value.boolean = EditorGUILayout.Toggle("Active", value.boolean); }
            if (EditorGUI.EndChangeCheck()) controller.Edit("Edit Visual Script Node", () =>
            {
                node.breakpoint = breakpoint;
                node.variable = variable;
                node.secondaryVariable = secondary;
                node.text = text;
                node.value = value;
                node.comparison = comparison;
                node.relative = relative;
                node.graphPath = graphPath;
                node.actionId = actionId;
            });
        }
    }

    private void DrawVariables(VisualScriptGraph graph)
    {
        GUILayout.Label("Variables", EditorStyles.boldLabel);
        for (var index = 0; index < graph.variables.Count; index++)
        {
            var variable = graph.variables[index];
            GUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField("Name", variable.name);
                var kind = (VisualScriptValueKind)EditorGUILayout.EnumPopup("Type", variable.value.kind);
                var value = variable.value.Clone();
                value.kind = kind;
                DrawValue(value, kind, "Default");
                if (EditorGUI.EndChangeCheck()) controller.Edit("Edit Visual Script Variable", () =>
                {
                    variable.name = name.Trim();
                    variable.value = value;
                });
                if (GUILayout.Button("Remove")) { controller.RemoveVariable(index); GUILayout.EndVertical(); break; }
            }
            GUILayout.EndVertical();
        }
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (GUILayout.Button("Add Variable")) controller.AddVariable();
    }

    private void DrawDebugger(VisualScriptGraph graph)
    {
        GUILayout.Space(12);
        GUILayout.Label("Runtime Debugger", EditorStyles.boldLabel);
        var runners = VisualScriptDebugHub.GetRunners().Where(item => SameGraph(item.graph, graph)).ToArray();
        if (runners.Length == 0)
        {
            GUILayout.Label("No live runner for this graph.", EditorStyles.wordWrappedLabel);
            return;
        }
        var index = Math.Max(0, Array.FindIndex(runners, item => item.Id == model.RunnerId));
        index = EditorGUILayout.Popup("Runner", index, runners.Select(item => item.gameObject.name).ToArray());
        var runner = runners[Math.Clamp(index, 0, runners.Length - 1)];
        model.RunnerId = runner.Id;
        var snapshot = runner.CreateDebugSnapshot();
        GUILayout.Label($"{snapshot.Status} | {snapshot.EventName} | step {snapshot.StepCount}");
        if (snapshot.Status == VisualScriptExecutionStatus.Paused)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Continue")) runner.ContinueExecution();
            if (GUILayout.Button("Step")) runner.StepExecution();
            if (GUILayout.Button("Stop")) runner.StopExecution();
            GUILayout.EndHorizontal();
        }
        foreach (var (name, value) in snapshot.Variables)
            GUILayout.Label(name + " = " + value, EditorStyles.miniLabel);
        if (snapshot.Error.Length > 0) EditorGUILayout.HelpBox(snapshot.Error);
    }

    private void ShowCreateMenu(Vector2 position)
    {
        if (!EditorAssetWritePolicy.CanWrite || model.Asset is null) return;
        var menu = new GenericMenu();
        foreach (var kind in Enum.GetValues<VisualScriptNodeKind>())
        {
            var captured = kind;
            menu.AddItem(new GUIContent(Title(kind)), false, () => controller.Add(captured, position));
        }
        menu.ShowAsContext();
    }

    private VisualScriptRunner? SelectedRunner() => model.RunnerId == Guid.Empty ? null : VisualScriptDebugHub.Find(model.RunnerId);
    private static IReadOnlyList<string> Inputs(VisualScriptNodeKind kind) => VisualScriptGraph.IsEvent(kind) ? [] : ["In"];
    private static bool SameGraph(VisualScriptGraph? left, VisualScriptGraph right) => ReferenceEquals(left, right) ||
        (left is not null && left.assetPath.Length > 0 && string.Equals(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase));
    private static string Title(VisualScriptNodeKind kind) => kind switch
    {
        VisualScriptNodeKind.StartEvent => "Event / Start",
        VisualScriptNodeKind.UpdateEvent => "Event / Update",
        VisualScriptNodeKind.CustomEvent => "Event / Custom",
        VisualScriptNodeKind.SetVariable => "Variable / Set",
        VisualScriptNodeKind.AddNumber => "Variable / Add Number",
        VisualScriptNodeKind.CompareNumber => "Condition / Compare Number",
        VisualScriptNodeKind.Branch => "Condition / Branch",
        VisualScriptNodeKind.SetPosition => "Action / Set Position",
        VisualScriptNodeKind.Log => "Action / Log",
        _ => "Flow / Stop"
    };
    private static Color32 NodeColor(VisualScriptNode node)
    {
        if (node.breakpoint) return new(112, 58, 61, 255);
        if (VisualScriptGraph.IsEvent(node.kind)) return new(52, 94, 72, 255);
        if (node.kind is VisualScriptNodeKind.Branch or VisualScriptNodeKind.CompareNumber) return new(95, 78, 43, 255);
        if (node.kind is VisualScriptNodeKind.SetVariable or VisualScriptNodeKind.AddNumber) return new(52, 76, 104, 255);
        return new(66, 67, 74, 255);
    }
    private static string Detail(VisualScriptNode node) => node.kind switch
    {
        VisualScriptNodeKind.CustomEvent => node.text,
        VisualScriptNodeKind.Branch or VisualScriptNodeKind.SetVariable or VisualScriptNodeKind.AddNumber => node.variable,
        VisualScriptNodeKind.CompareNumber => $"{node.variable} {node.comparison} {node.value.number}",
        VisualScriptNodeKind.Log => node.text,
        VisualScriptNodeKind.SetPosition => node.value.vector.ToString(),
        _ => string.Empty
    };
    private static string VariablePopup(string label, string current, IReadOnlyList<VisualScriptVariable> variables)
    {
        var names = variables.Select(item => item.name).ToArray();
        if (names.Length == 0) { GUILayout.Label(label + ": no variables"); return string.Empty; }
        var index = Math.Max(0, Array.IndexOf(names, current));
        return names[EditorGUILayout.Popup(label, index, names)];
    }
    private static void DrawValue(VisualScriptValue value, VisualScriptValueKind kind, string label)
    {
        switch (kind)
        {
            case VisualScriptValueKind.Boolean: value.boolean = EditorGUILayout.Toggle(label, value.boolean); break;
            case VisualScriptValueKind.Number: value.number = (Fix64)EditorGUILayout.FloatField(label, (float)value.number); break;
            case VisualScriptValueKind.String: value.text = EditorGUILayout.TextField(label, value.text); break;
            case VisualScriptValueKind.Vector2: value.vector = EditorGUILayout.Vector2Field(label, value.vector); break;
        }
    }
}

public sealed class VisualScriptWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public VisualScriptGraph? Asset { get; set; }
    public HashSet<string> Selected { get; set; } = new(StringComparer.Ordinal);
    public string? SelectedConnection { get; set; }
    public bool Frame { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public Guid RunnerId { get; set; }
}

public sealed class VisualScriptWindowController(EditorWindowMvcContext context, VisualScriptWindowModel model)
    : EditorWindowController<VisualScriptWindow, VisualScriptWindowModel>(context, model)
{
    public void Select(VisualScriptGraph? graph)
    {
        Model.Asset = graph;
        Model.Selected.Clear();
        Model.SelectedConnection = null;
        Model.Frame = true;
        Model.Message = string.Empty;
        View.Repaint();
    }

    public void Edit(string label, Action action)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        if (Model.Asset is not { } graph) return;
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(graph, label);
        action();
        EditorUtility.SetDirty(graph);
        View.Repaint();
    }

    public void BeginNodeMove()
    {
        if (!EditorAssetWritePolicy.CanWrite || Model.Asset is not { } graph) return;
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(graph, "Move Visual Script Nodes");
    }

    public void MoveNodes(IReadOnlyDictionary<string, Vector2> positions)
    {
        if (!EditorAssetWritePolicy.CanWrite || Model.Asset is not { } graph) return;
        foreach (var node in graph.nodes)
            if (positions.TryGetValue(node.id, out var position)) node.position = position;
        EditorUtility.SetDirty(graph);
        View.Repaint();
    }

    public void Add(VisualScriptNodeKind kind, Vector2 position) => Edit("Add Visual Script Node", () =>
    {
        var graph = Model.Asset!;
        var first = graph.variables.FirstOrDefault();
        var boolean = graph.variables.FirstOrDefault(item => item.value.kind == VisualScriptValueKind.Boolean);
        var number = graph.variables.FirstOrDefault(item => item.value.kind == VisualScriptValueKind.Number);
        var node = new VisualScriptNode
        {
            kind = kind,
            position = position,
            variable = kind is VisualScriptNodeKind.AddNumber or VisualScriptNodeKind.CompareNumber
                ? number?.name ?? first?.name ?? string.Empty
                : boolean?.name ?? first?.name ?? string.Empty,
            secondaryVariable = boolean?.name ?? string.Empty,
            text = kind == VisualScriptNodeKind.CustomEvent ? "Event" : kind == VisualScriptNodeKind.Log ? "Message" : string.Empty,
            value = kind == VisualScriptNodeKind.SetPosition ? VisualScriptValue.Vector(Vector2.zero) :
                kind is VisualScriptNodeKind.AddNumber or VisualScriptNodeKind.CompareNumber ? VisualScriptValue.Number(1) :
                first?.value.Clone() ?? VisualScriptValue.Boolean(false)
        };
        graph.nodes.Add(node);
        Model.Selected = [node.id];
        Model.SelectedConnection = null;
    });

    public void Connect(string source, string sourcePort, string destination, string destinationPort)
    {
        if (Model.Asset is not { } graph || source == destination) return;
        var destinationNode = graph.nodes.FirstOrDefault(item => item.id == destination);
        if (destinationNode is null || VisualScriptGraph.IsEvent(destinationNode.kind)) return;
        Edit("Connect Visual Script Nodes", () =>
        {
            graph.connections.RemoveAll(item => item.destination == destination && item.destinationPort == destinationPort);
            graph.connections.Add(new VisualScriptConnection
            {
                source = source,
                sourcePort = sourcePort,
                destination = destination,
                destinationPort = destinationPort
            });
        });
    }

    public void DeleteSelection() => Edit("Delete Visual Script Selection", () =>
    {
        var graph = Model.Asset!;
        if (Model.SelectedConnection is { } selectedConnection)
            graph.connections.RemoveAll(item => item.id == selectedConnection);
        else
        {
            var removed = graph.nodes.Where(item => Model.Selected.Contains(item.id)).Select(item => item.id)
                .ToHashSet(StringComparer.Ordinal);
            graph.nodes.RemoveAll(item => removed.Contains(item.id));
            graph.connections.RemoveAll(item => removed.Contains(item.source) || removed.Contains(item.destination));
        }
        Model.Selected.Clear();
        Model.SelectedConnection = null;
    });

    public void AddVariable() => Edit("Add Visual Script Variable", () =>
    {
        var names = Model.Asset!.variables.Select(item => item.name).ToHashSet(StringComparer.Ordinal);
        var index = 1;
        while (names.Contains("variable" + index)) index++;
        Model.Asset.variables.Add(new VisualScriptVariable { name = "variable" + index,
            value = VisualScriptValue.Boolean(false) });
    });

    public void RemoveVariable(int index) => Edit("Remove Visual Script Variable", () =>
    {
        var graph = Model.Asset!;
        if (index < 0 || index >= graph.variables.Count) return;
        var name = graph.variables[index].name;
        graph.variables.RemoveAt(index);
        foreach (var node in graph.nodes)
        {
            if (node.variable == name) node.variable = string.Empty;
            if (node.secondaryVariable == name) node.secondaryVariable = string.Empty;
        }
    });

    public bool Validate()
    {
        try
        {
            Model.Asset?.Validate();
            Model.Message = "Graph validation passed.";
            return true;
        }
        catch (Exception exception)
        {
            Model.Message = exception.Message;
            return false;
        }
        finally { View.Repaint(); }
    }

    public bool Save()
    {
        if (Model.Asset is not { } graph) return false;
        try
        {
            EditorAssetWritePolicy.EnsureCanWrite("Save Visual Script");
            graph.Validate();
            var path = graph.assetPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
                path = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/', '\\') + "/New Visual Script.visualscript.yaml");
            }
            graph.Save(AssetDatabase.ResolveAssetPath(path));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Model.Asset = AssetDatabase.LoadAssetAtPath<VisualScriptGraph>(path) ?? graph;
            Model.Message = "Saved " + path;
            EditorUtility.SetDirty(Model.Asset);
            return true;
        }
        catch (Exception exception)
        {
            Model.Message = exception.Message;
            return false;
        }
        finally { View.Repaint(); }
    }
}
