using BEngine.Physics2D;

namespace BEngine.Editor;

[CustomEditor(typeof(SpriteShapeRenderer2D))]
public sealed class SpriteShape2DEditor : Editor
{
    private static readonly Color TangentColor = new(1, 1, 0, 1);
    private int _control;
    private int _dragPoint = -1;
    private int _dragTangent;
    private int _undoGroup = -1;
    private bool _sceneDragging;
    private string _message = string.Empty;
    public bool editSpline { get; set; } = true;
    public int selectedPoint { get; set; }
    private SpriteShapeRenderer2D Shape => (SpriteShapeRenderer2D)target;

    protected override void OnEnable() => SceneView.duringSceneGui += DrawScene;
    protected override void OnDisable() { EndDrag(true); SceneView.duringSceneGui -= DrawScene; }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (Shape.shape is not { } asset)
        {
            EditorGUILayout.HelpBox("Assign a Sprite Shape asset to edit its spline.");
            return;
        }
        selectedPoint = Math.Clamp(selectedPoint, 0, asset.Points.Count - 1);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            editSpline = EditorGUILayout.Toggle("Edit spline in Scene", editSpline);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Point")) AddPointAfter(selectedPoint);
            if (GUILayout.Button("Remove Point")) RemovePoint(selectedPoint);
            if (GUILayout.Button("Rebuild Collider")) RebuildCollider();
            GUILayout.EndHorizontal();
        }
        if (_message.Length > 0) EditorGUILayout.HelpBox(_message, MessageType.Info);
    }

    public bool SetPointPosition(int index, Vector2 localPosition) => Edit("Move sprite shape point", asset =>
    {
        if ((uint)index >= (uint)asset.Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
        asset.Points[index].Position = localPosition;
    });

    public bool SetTangent(int index, bool left, Vector2 localOffset) => Edit("Edit sprite shape tangent", asset =>
    {
        if ((uint)index >= (uint)asset.Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
        asset.SetTangent(index, left, localOffset);
    });

    public bool AddPointAfter(int index) => Edit("Add sprite shape point", asset =>
    {
        index = Math.Clamp(index, 0, asset.Points.Count - 1);
        var next = index + 1;
        var other = next < asset.Points.Count ? asset.Points[next] : asset.Closed ? asset.Points[0] : asset.Points[index];
        asset.Points.Insert(next, new SpriteShapePoint
        {
            Position = (asset.Points[index].Position + other.Position) * Fix64.Half,
            Mode = SpriteShapeTangentMode.Linear
        });
        selectedPoint = next;
    });

    public bool RemovePoint(int index) => Edit("Remove sprite shape point", asset =>
    {
        var minimum = asset.Closed ? 3 : 2;
        if (asset.Points.Count <= minimum) throw new InvalidOperationException($"The shape requires at least {minimum} points.");
        asset.Points.RemoveAt(Math.Clamp(index, 0, asset.Points.Count - 1));
        selectedPoint = Math.Clamp(index - 1, 0, asset.Points.Count - 1);
    });

    public bool SetClosed(bool closed) => Edit("Change sprite shape closure", asset => asset.Closed = closed);

    public bool RebuildCollider()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Rebuild sprite shape collider");
        Undo.RecordObject(Shape, "Rebuild sprite shape collider");
        var result = Shape.RebuildCollider();
        EditorUtility.SetDirty(Shape);
        if (Shape.gameObject.scene is { } scene) EditorSceneManager.MarkSceneDirty(scene);
        _message = result ? "Collider synchronized with the sampled spline." : "Collider generation is disabled.";
        return result;
    }

    private void DrawScene(SceneViewToolContext context)
    {
        if (target.isDestroyed || Shape.shape is not { } asset || !editSpline ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            !ReferenceEquals(Selection.activeGameObject, Shape.gameObject) ||
            SceneVisibilityManager.instance.IsHidden(Shape.gameObject))
        {
            EndDrag(true);
            return;
        }
        context.suppressDefaultTools = true;
        _control = GUIUtility.GetControlID(GetInstanceID(), FocusType.Passive);
        var current = Event.current;
        if (current.type == EventType.Repaint) DrawSpline(context, asset);
        if (current.type == EventType.MouseDown && current.button == 0 && context.pointerInViewport && GUIUtility.hotControl == 0)
        {
            var hit = HitTest(context, asset, current.mousePosition);
            if (hit.Point >= 0)
            {
                selectedPoint = hit.Point; _dragPoint = hit.Point; _dragTangent = hit.Tangent;
                _sceneDragging = true;
                GUIUtility.hotControl = _control; current.Use();
            }
        }
        else if (_dragPoint >= 0 && GUIUtility.hotControl == _control)
        {
            if (current.type == EventType.MouseDrag)
            {
                var local = Shape.transform.InverseTransformPoint(context.GUIToWorld(current.mousePosition));
                if (_dragTangent == 0) SetPointPosition(_dragPoint, local);
                else SetTangent(_dragPoint, _dragTangent < 0, local - asset.Points[_dragPoint].Position);
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0) { EndDrag(false); current.Use(); }
            else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape) { EndDrag(true); current.Use(); }
        }
    }

    private void DrawSpline(SceneViewToolContext context, SpriteShapeAsset asset)
    {
        var outline = SpriteShapeGeometryBuilder.Sample(asset);
        var segments = asset.Closed ? outline.Length : outline.Length - 1;
        for (var index = 0; index < segments; index++)
            context.DrawWorldLine(Shape.transform.TransformPoint(outline[index]),
                Shape.transform.TransformPoint(outline[(index + 1) % outline.Length]), new Color(0, 1, 1, 1), 2);
        for (var index = 0; index < asset.Points.Count; index++)
        {
            var point = asset.Points[index];
            var anchor = Shape.transform.TransformPoint(point.Position);
            DrawHandle(context, anchor, index == selectedPoint ? TangentColor : Color.white, 5);
            if (point.Mode == SpriteShapeTangentMode.Linear) continue;
            var left = Shape.transform.TransformPoint(point.Position + point.LeftTangent);
            var right = Shape.transform.TransformPoint(point.Position + point.RightTangent);
            context.DrawWorldLine(anchor, left, TangentColor, 1);
            context.DrawWorldLine(anchor, right, TangentColor, 1);
            DrawHandle(context, left, TangentColor, 4); DrawHandle(context, right, TangentColor, 4);
        }
    }

    private static void DrawHandle(SceneViewToolContext context, Vector2 world, Color color, Fix64 size)
    {
        var point = context.WorldToGUI(world);
        if (context.viewport.Contains(point)) GUI.DrawRect(new Rect(point.x - size, point.y - size, size * 2, size * 2), color);
    }

    private (int Point, int Tangent) HitTest(SceneViewToolContext context, SpriteShapeAsset asset, Vector2 pointer)
    {
        for (var index = 0; index < asset.Points.Count; index++)
        {
            var point = asset.Points[index];
            if (Vector2.Distance(context.WorldToGUI(Shape.transform.TransformPoint(point.Position)), pointer) <= 10) return (index, 0);
            if (point.Mode == SpriteShapeTangentMode.Linear) continue;
            if (Vector2.Distance(context.WorldToGUI(Shape.transform.TransformPoint(point.Position + point.LeftTangent)), pointer) <= 9) return (index, -1);
            if (Vector2.Distance(context.WorldToGUI(Shape.transform.TransformPoint(point.Position + point.RightTangent)), pointer) <= 9) return (index, 1);
        }
        return (-1, 0);
    }

    private bool Edit(string label, Action<SpriteShapeAsset> action)
    {
        if (Shape.shape is not { } asset) return false;
        EditorAssetWritePolicy.EnsureCanWrite(label);
        var closeGroup = !_sceneDragging;
        if (_undoGroup < 0)
        {
            Undo.IncrementCurrentGroup(); _undoGroup = Undo.GetCurrentGroup(); Undo.RecordObject(asset, label);
        }
        try
        {
            action(asset); asset.Validate(); EditorUtility.SetDirty(asset);
            if (Shape.generateCollider) Shape.RebuildCollider();
            _message = string.Empty; SceneView.RepaintAll();
            if (closeGroup) EndUndoGroup(false);
            return true;
        }
        catch (Exception error)
        {
            Undo.RevertAllDownToGroup(_undoGroup); _undoGroup = -1; _message = error.Message; return false;
        }
    }

    private void EndDrag(bool cancel)
    {
        if (_dragPoint < 0 && _undoGroup < 0) return;
        EndUndoGroup(cancel);
        if (GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        _dragPoint = -1; _dragTangent = 0; _sceneDragging = false; SceneView.RepaintAll();
    }

    private void EndUndoGroup(bool cancel)
    {
        if (_undoGroup < 0) return;
        if (cancel) Undo.RevertAllDownToGroup(_undoGroup); else Undo.CollapseUndoOperations(_undoGroup);
        Undo.IncrementCurrentGroup();
        _undoGroup = -1;
    }
}

[CustomEditor(typeof(SpriteShapeAsset))]
public sealed class SpriteShapeAssetEditor : BAssetEditor
{
    private SpriteShapeAsset? _operand;
    private PolygonBooleanOperation2D _operation;
    private string _message = string.Empty;
    public override void OnInspectorGUI()
    {
        var asset = (SpriteShapeAsset)target;
        base.OnInspectorGUI();
        GUILayout.Label($"{asset.Points.Count} control points, {(asset.Closed ? "closed" : "open")}, {asset.SamplesPerSegment} samples/segment",
            EditorStyles.wordWrappedLabel);
        try
        {
            var geometry = SpriteShapeGeometryBuilder.Build(asset);
            GUILayout.Label($"{geometry.Outline.Length} outline points, {geometry.EdgeTriangles.Length / 3} edge triangles, {geometry.FillTriangles.Length / 3} fill triangles");
        }
        catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            _operand = EditorGUILayout.ObjectField("Boolean operand", _operand, typeof(SpriteShapeAsset), false) as SpriteShapeAsset;
            _operation = (PolygonBooleanOperation2D)EditorGUILayout.EnumPopup("Operation", _operation);
            using (new EditorGUI.DisabledScope(_operand is null || !asset.Closed || !_operand.Closed))
                if (GUILayout.Button("Create Boolean Assets"))
                {
                    try
                    {
                        var folder = Path.GetDirectoryName(asset.assetPath)?.Replace('\\', '/') ?? "Assets";
                        var assets = asset.Combine(_operand!, _operation);
                        foreach (var item in assets)
                        {
                            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + item.name + ".spriteshape.yaml");
                            AssetDatabase.CreateAsset(item, path);
                        }
                        AssetDatabase.SaveAssets();
                        if (assets.Count > 0) Selection.activeObject = assets[0];
                        _message = assets.Count + " polygon assets created. Curves were baked; source assets are preserved.";
                    }
                    catch (Exception error) { _message = error.Message; }
                }
        }
        if (_message.Length > 0) EditorGUILayout.HelpBox(_message);
    }
}
