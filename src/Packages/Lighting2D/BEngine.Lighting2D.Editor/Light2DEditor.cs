namespace BEngine.Editor;

[CustomEditor(typeof(Light2D))]
public sealed class Light2DEditor : Editor
{
    private int _selected, _dragVertex = -1, _dragGroup = -1, _control;
    private string _message = "";
    public bool editShape { get; set; }
    private Light2D Light => (Light2D)target;
    protected override void OnEnable() => SceneView.duringSceneGui += DrawScene;
    protected override void OnDisable() { EndDrag(true); SceneView.duringSceneGui -= DrawScene; }

    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        foreach (var property in serializedObject.GetVisibleProperties())
            if (property.name != "shapePath") EditorGUILayout.PropertyField(property, includeChildren: true);
        serializedObject.ApplyModifiedProperties();
        if (Light.lightType != Light2DType.Freeform) return;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            editShape = EditorGUILayout.Toggle("Edit outline in Scene", editShape);
            for (var index = 0; index < Light.shapePath.Count; index++)
            {
                var changed = EditorGUILayout.Vector2Field("Point " + index, Light.shapePath[index]);
                if (changed != Light.shapePath[index]) { var at = index; TryEdit(() => SetVertex(at, changed)); }
            }
            _selected = Math.Clamp(EditorGUILayout.IntField("Selected point", _selected), 0, Light.shapePath.Count - 1);
            using (new EditorGUI.DisabledScope(Light.shapePath.Count >= 128))
                if (GUILayout.Button("Insert midpoint after selected")) TryEdit(() => InsertAfter(_selected));
            using (new EditorGUI.DisabledScope(Light.shapePath.Count <= 3))
                if (GUILayout.Button("Remove selected point")) TryEdit(() => RemoveVertex(_selected));
        }
        if (_message.Length > 0) EditorGUILayout.HelpBox(_message, MessageType.Warning);
    }

    public void SetVertex(int index, Vector2 point)
    {
        var points = Light.shapePath.ToList(); points[index] = point; SetShape(points, "Move light vertex");
    }
    public void InsertAfter(int index)
    {
        var points = Light.shapePath.ToList(); points.Insert(index + 1, (points[index] + points[(index + 1) % points.Count]) / 2);
        SetShape(points, "Insert light vertex"); _selected = index + 1;
    }
    public void RemoveVertex(int index)
    {
        var points = Light.shapePath.ToList(); points.RemoveAt(index); SetShape(points, "Remove light vertex");
        _selected = Math.Min(index, points.Count - 1);
    }
    private void SetShape(List<Vector2> points, string label)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        if (Light.shapePath.SequenceEqual(points)) return;
        var previous = Light.shapePath; Light.shapePath = points;
        try { Light.OnValidate(); }
        finally { Light.shapePath = previous; }
        if (_dragVertex < 0) { Undo.IncrementCurrentGroup(); Undo.RecordObject(Light, label); }
        else if (_dragGroup < 0)
        { Undo.IncrementCurrentGroup(); _dragGroup = Undo.GetCurrentGroup(); Undo.RecordObject(Light, label); }
        Light.shapePath = points; EditorUtility.SetDirty(Light);
        if (Light.gameObject.scene is { } scene) EditorSceneManager.MarkSceneDirty(scene);
        _message = ""; SceneView.RepaintAll();
    }

    private void DrawScene(SceneViewToolContext context)
    {
        if (target.isDestroyed || !ReferenceEquals(Selection.activeGameObject, Light.gameObject) ||
            !editShape || Light.lightType != Light2DType.Freeform || EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneVisibilityManager.instance.IsHidden(Light.gameObject))
        { EndDrag(true); return; }
        context.suppressDefaultTools = true;
        var current = Event.current;
        _control = GUIUtility.GetControlID(GetInstanceID(), FocusType.Passive);
        if (current.type == EventType.Repaint)
        {
            for (var index = 0; index < Light.shapePath.Count; index++)
            {
                var point = Light.transform.TransformPoint(Light.shapePath[index]);
                var next = Light.transform.TransformPoint(Light.shapePath[(index + 1) % Light.shapePath.Count]);
                context.DrawWorldLine(point, next, new Color(1, 1, 0, 1), 2);
                var gui = context.WorldToGUI(point);
                if (context.viewport.Contains(gui)) GUI.DrawRect(new(gui.x - 4, gui.y - 4, 8, 8), index == _selected ? Color.white : new Color(1, 1, 0, 1));
            }
        }
        if (current.type == EventType.MouseDown && current.button == 0 && context.pointerInViewport && GUIUtility.hotControl == 0)
        {
            for (var index = 0; index < Light.shapePath.Count; index++)
                if (Vector2.Distance(context.WorldToGUI(Light.transform.TransformPoint(Light.shapePath[index])), current.mousePosition) <= 9)
                { _selected = _dragVertex = index; _dragGroup = -1; GUIUtility.hotControl = _control; current.Use(); break; }
        }
        else if (_dragVertex >= 0 && GUIUtility.hotControl == _control)
        {
            if (current.type == EventType.MouseDrag)
            {
                if (Light.transform.lossyScale.x != 0 && Light.transform.lossyScale.y != 0)
                    TryEdit(() => SetVertex(_dragVertex, Light.transform.InverseTransformPoint(context.GUIToWorld(current.mousePosition))));
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0) { EndDrag(false); current.Use(); }
            else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape) { EndDrag(true); current.Use(); }
        }
    }

    private void EndDrag(bool cancel)
    {
        if (_dragVertex < 0) return;
        if (_dragGroup >= 0)
        {
            if (cancel) Undo.RevertAllDownToGroup(_dragGroup);
            else Undo.CollapseUndoOperations(_dragGroup);
            Undo.IncrementCurrentGroup();
        }
        if (GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        _dragVertex = _dragGroup = -1; SceneView.RepaintAll();
    }
    private void TryEdit(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { _message = error.Message; }
    }
}
