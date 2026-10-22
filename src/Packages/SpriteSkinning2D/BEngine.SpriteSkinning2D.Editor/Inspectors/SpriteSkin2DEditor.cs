namespace BEngine.Editor;

[CustomEditor(typeof(SpriteSkin2D))]
public sealed class SpriteSkin2DEditor : Editor
{
    private int _control, _dragVertex = -1, _undoGroup = -1;
    private string _message = string.Empty;
    public bool editMesh { get; set; }
    public bool paintWeights { get; set; }
    public int selectedBone { get; set; }
    public Fix64 brushRadius { get; set; } = Fix64.Parse("0.35");
    public Fix64 brushStrength { get; set; } = Fix64.Parse("0.2");
    private SpriteSkin2D Skin => (SpriteSkin2D)target;

    protected override void OnEnable() => SceneView.duringSceneGui += DrawScene;
    protected override void OnDisable() { EndDrag(cancel: true); SceneView.duringSceneGui -= DrawScene; }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (Skin.skin is not { } asset)
        {
            EditorGUILayout.HelpBox("Assign a Sprite Skin asset to edit and render a weighted mesh.");
            return;
        }
        var attachments = asset.Attachments.Select(item => item.Name).ToArray();
        if (attachments.Length > 0)
        {
            var current = Math.Max(0, Array.IndexOf(attachments, Skin.attachment));
            var selected = EditorGUILayout.Popup("Attachment", current, attachments);
            if (selected != current && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Undo.RecordObject(Skin, "Change sprite skin attachment");
                Skin.SetAttachment(attachments[selected]);
                EditorUtility.SetDirty(Skin);
            }
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Skin Editor")) SpriteSkinningWindow.Open(asset);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Rebind"))
            {
                Undo.RecordObject(Skin, "Rebind sprite skin");
                _message = Skin.Rebind() ? "Bone bindings updated." : "Some bone names could not be resolved.";
                EditorUtility.SetDirty(Skin);
            }
            if (GUILayout.Button("Create Skeleton"))
            {
                var window = EditorWindow.GetWindow<SpriteSkinningWindow>("Sprite Skinning 2D");
                window.SelectAsset(asset);
                _message = window.BindSelectedRenderer() ? "Skeleton created and bound." : "Could not create the skeleton.";
                window.Show();
            }
        }
        GUILayout.EndHorizontal();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            editMesh = EditorGUILayout.Toggle("Edit mesh in Scene", editMesh);
            paintWeights = EditorGUILayout.Toggle("Paint weights", paintWeights);
            if (paintWeights)
            {
                var names = asset.Bones.Select(item => item.Name).ToArray();
                selectedBone = EditorGUILayout.Popup("Paint bone", Math.Clamp(selectedBone, 0, names.Length - 1), names);
                brushRadius = (Fix64)EditorGUILayout.Slider("Brush radius", (float)brushRadius, 0.02f, 5f);
                brushStrength = (Fix64)EditorGUILayout.Slider("Brush strength", (float)brushStrength, 0.01f, 1f);
            }
        }
        if (_message.Length > 0) EditorGUILayout.HelpBox(_message, MessageType.Info);
    }

    private void DrawScene(SceneViewToolContext context)
    {
        if (target.isDestroyed || Skin.skin is not { } asset || !editMesh ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            !ReferenceEquals(Selection.activeGameObject, Skin.gameObject) ||
            SceneVisibilityManager.instance.IsHidden(Skin.gameObject))
        {
            EndDrag(cancel: true);
            return;
        }
        context.suppressDefaultTools = true;
        selectedBone = Math.Clamp(selectedBone, 0, asset.Bones.Count - 1);
        var current = Event.current;
        _control = GUIUtility.GetControlID(GetInstanceID(), FocusType.Passive);
        if (current.type == EventType.Repaint)
        {
            for (var index = 0; index < asset.Triangles.Count; index += 3)
            {
                var a = Skin.transform.TransformPoint(asset.Vertices[asset.Triangles[index]].Position);
                var b = Skin.transform.TransformPoint(asset.Vertices[asset.Triangles[index + 1]].Position);
                var c = Skin.transform.TransformPoint(asset.Vertices[asset.Triangles[index + 2]].Position);
                context.DrawWorldLine(a, b, new Color(0, 1, 1, 1), 1);
                context.DrawWorldLine(b, c, new Color(0, 1, 1, 1), 1);
                context.DrawWorldLine(c, a, new Color(0, 1, 1, 1), 1);
            }
            for (var index = 0; index < asset.Vertices.Count; index++)
            {
                var point = context.WorldToGUI(Skin.transform.TransformPoint(asset.Vertices[index].Position));
                if (!context.viewport.Contains(point)) continue;
                var weight = asset.Vertices[index].Weights.FirstOrDefault(item => item.BoneIndex == selectedBone)?.Weight ?? Fix64.Zero;
                var color = paintWeights ? new Color(weight, Fix64.Zero, Fix64.One - weight, Fix64.One) : Color.white;
                GUI.DrawRect(new Rect(point.x - 4, point.y - 4, 8, 8), color);
            }
        }
        if (current.type == EventType.MouseDown && current.button == 0 && context.pointerInViewport && GUIUtility.hotControl == 0)
        {
            var nearest = FindNearestVertex(context, asset, current.mousePosition, 10);
            if (paintWeights || nearest >= 0)
            {
                _dragVertex = nearest;
                _undoGroup = -1;
                GUIUtility.hotControl = _control;
                if (paintWeights) Paint(context.GUIToWorld(current.mousePosition));
                current.Use();
            }
        }
        else if (GUIUtility.hotControl == _control && (paintWeights || _dragVertex >= 0))
        {
            if (current.type == EventType.MouseDrag)
            {
                if (paintWeights) Paint(context.GUIToWorld(current.mousePosition));
                else MoveVertex(_dragVertex, Skin.transform.InverseTransformPoint(context.GUIToWorld(current.mousePosition)));
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

    public bool MoveVertex(int index, Vector2 localPosition)
    {
        if (Skin.skin is not { } asset || (uint)index >= (uint)asset.Vertices.Count) return false;
        return EditAsset(asset, "Move sprite skin vertex", () => asset.Vertices[index].Position = localPosition);
    }

    public bool Paint(Vector2 worldPosition)
    {
        if (Skin.skin is not { } asset) return false;
        var local = Skin.transform.InverseTransformPoint(worldPosition);
        return EditAsset(asset, "Paint sprite skin weights", () =>
        {
            var changed = false;
            foreach (var vertex in asset.Vertices)
            {
                var distance = Vector2.Distance(vertex.Position, local);
                if (distance > brushRadius) continue;
                var falloff = Fix64.One - distance / Fix64.Max(Fix64.Parse("0.001"), brushRadius);
                var existing = vertex.Weights.FirstOrDefault(item => item.BoneIndex == selectedBone)?.Weight ?? Fix64.Zero;
                SpriteSkinningWindow.ApplyWeight(vertex, selectedBone,
                    existing + brushStrength * Fix64.Max(Fix64.Parse("0.05"), falloff));
                changed = true;
            }
            if (!changed) throw new InvalidOperationException("The brush does not cover a mesh vertex.");
        });
    }

    private bool EditAsset(SpriteSkinAsset asset, string label, Action action)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        if (_undoGroup < 0)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.RecordObject(asset, label);
        }
        try
        {
            action();
            asset.Validate();
            EditorUtility.SetDirty(asset);
            _message = string.Empty;
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception error)
        {
            Undo.RevertAllDownToGroup(_undoGroup);
            _undoGroup = -1;
            _message = error.Message;
            return false;
        }
    }

    private static int FindNearestVertex(SceneViewToolContext context, SpriteSkinAsset asset, Vector2 pointer, Fix64 radius)
    {
        var skin = Selection.activeGameObject?.GetComponent<SpriteSkin2D>();
        if (skin is null) return -1;
        var nearest = -1;
        var distance = radius;
        for (var index = 0; index < asset.Vertices.Count; index++)
        {
            var candidate = Vector2.Distance(context.WorldToGUI(
                skin.transform.TransformPoint(asset.Vertices[index].Position)), pointer);
            if (candidate > distance) continue;
            distance = candidate;
            nearest = index;
        }
        return nearest;
    }

    private void EndDrag(bool cancel)
    {
        if (_dragVertex < 0 && GUIUtility.hotControl != _control) return;
        if (_undoGroup >= 0)
        {
            if (cancel) Undo.RevertAllDownToGroup(_undoGroup);
            else Undo.CollapseUndoOperations(_undoGroup);
            Undo.IncrementCurrentGroup();
        }
        if (GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        _dragVertex = -1;
        _undoGroup = -1;
        SceneView.RepaintAll();
    }
}

[CustomEditor(typeof(SpriteSkinAsset))]
public sealed class SpriteSkinAssetEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var asset = (SpriteSkinAsset)target;
        if (GUILayout.Button("Open Sprite Skinning")) SpriteSkinningWindow.Open(asset);
        GUILayout.Label($"{asset.Bones.Count} bones, {asset.Vertices.Count} vertices, " +
                        $"{asset.Triangles.Count / 3} triangles, {asset.Attachments.Count} attachments",
            EditorStyles.wordWrappedLabel);
        try { asset.Validate(); }
        catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
    }
}
