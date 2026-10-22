namespace BEngine.Editor;

[CustomEditor(typeof(Bone2D))]
public sealed class Bone2DEditor : Editor
{
    private int _control, _undoGroup = -1;
    private bool _dragging;
    public bool editInScene { get; set; } = true;
    private Bone2D Bone => (Bone2D)target;

    protected override void OnEnable() => SceneView.duringSceneGui += DrawScene;
    protected override void OnDisable() { EndDrag(cancel: true); SceneView.duringSceneGui -= DrawScene; }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            editInScene = EditorGUILayout.Toggle("Edit bone in Scene", editInScene);
    }

    private void DrawScene(SceneViewToolContext context)
    {
        if (target.isDestroyed || !editInScene || EditorApplication.isPlayingOrWillChangePlaymode ||
            !ReferenceEquals(Selection.activeGameObject, Bone.gameObject) ||
            SceneVisibilityManager.instance.IsHidden(Bone.gameObject))
        {
            EndDrag(cancel: true);
            return;
        }
        context.suppressDefaultTools = true;
        var current = Event.current;
        _control = GUIUtility.GetControlID(GetInstanceID(), FocusType.Passive);
        var start = Bone.transform.position;
        var tip = Bone.tip;
        if (current.type == EventType.Repaint)
        {
            context.DrawWorldLine(start, tip, Bone.color, 3);
            var point = context.WorldToGUI(tip);
            if (context.viewport.Contains(point)) GUI.DrawRect(new Rect(point.x - 5, point.y - 5, 10, 10), Color.white);
        }
        if (current.type == EventType.MouseDown && current.button == 0 && context.pointerInViewport &&
            GUIUtility.hotControl == 0 && Vector2.Distance(context.WorldToGUI(tip), current.mousePosition) <= 11)
        {
            _dragging = true;
            _undoGroup = -1;
            GUIUtility.hotControl = _control;
            current.Use();
        }
        else if (_dragging && GUIUtility.hotControl == _control)
        {
            if (current.type == EventType.MouseDrag)
            {
                SetTip(context.GUIToWorld(current.mousePosition));
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                EndDrag(cancel: false);
                current.Use();
            }
            else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                EndDrag(cancel: true);
                current.Use();
            }
        }
    }

    public void SetTip(Vector2 worldTip)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Edit 2D bone");
        var delta = worldTip - Bone.transform.position;
        if (delta.sqrMagnitude <= Fix64.Epsilon) return;
        if (_undoGroup < 0)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.RecordObjects([Bone, Bone.transform], "Edit 2D bone");
        }
        Bone.transform.rotation = Mathf.Atan2(delta.y, delta.x) * Fix64.Rad2Deg;
        Bone.length = delta.magnitude / Fix64.Max(Fix64.Parse("0.001"), Fix64.Abs(Bone.transform.lossyScale.x));
        EditorUtility.SetDirty(Bone);
        EditorUtility.SetDirty(Bone.transform);
        if (Bone.gameObject.scene is { } scene) EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();
    }

    private void EndDrag(bool cancel)
    {
        if (!_dragging) return;
        if (_undoGroup >= 0)
        {
            if (cancel) Undo.RevertAllDownToGroup(_undoGroup);
            else Undo.CollapseUndoOperations(_undoGroup);
            Undo.IncrementCurrentGroup();
        }
        if (GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        _dragging = false;
        _undoGroup = -1;
        SceneView.RepaintAll();
    }
}
