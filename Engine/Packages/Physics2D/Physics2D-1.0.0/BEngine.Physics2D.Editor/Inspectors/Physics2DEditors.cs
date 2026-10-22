using BEngine.Editor;

namespace BEngine.Physics2D.Editor;

[CustomEditor(typeof(Collider2D), true)]
public sealed class Collider2DEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var collider = (Collider2D)target;
        if (collider is PolygonCollider2D polygon)
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                if (GUILayout.Button("Add Hole")) AddHole();
            GUILayout.Label($"Cutout contours: {polygon.holes.Count}", EditorStyles.miniLabel);
        }
        if (collider is PolygonCollider2D or CompositeCollider2D)
        {
            try
            {
                if (collider is CompositeCollider2D composite)
                {
                    var paths = composite.GetGeometryPaths();
                    GUILayout.Label($"Result: {paths.Count(path => !path.isHole)} solids, {paths.Count(path => path.isHole)} holes", EditorStyles.miniLabel);
                }
                else _ = collider.bounds;
            }
            catch (InvalidDataException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        }
        if (collider.GetComponent<SpriteRenderer>() is not { } sprite || collider is not (BoxCollider2D or CircleCollider2D)) return;
        if (GUILayout.Button("Fit to Sprite"))
        {
            Undo.RecordObject(collider, "Fit Collider to Sprite");
            if (collider is BoxCollider2D box) box.size = new Vector2(Fix64.Abs(sprite.size.x), Fix64.Abs(sprite.size.y));
            else if (collider is CircleCollider2D circle) circle.radius = Fix64.Max(Fix64.Abs(sprite.size.x), Fix64.Abs(sprite.size.y)) / 2;
            collider.offset = new Vector2((Fix64.Half - sprite.pivot.x) * sprite.size.x, (Fix64.Half - sprite.pivot.y) * sprite.size.y);
            EditorUtility.SetDirty(collider); SceneView.RepaintAll();
        }
    }

    public void AddHole()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit collider holes outside Play mode.");
        if (target is not PolygonCollider2D polygon || polygon.points.Count < 3)
            throw new InvalidOperationException("A hole requires an outer polygon with at least three vertices.");
        var min = polygon.points.Aggregate(Vector2.Min); var max = polygon.points.Aggregate(Vector2.Max);
        var center = (min + max) / 2; var extent = (max - min) / 8;
        Undo.IncrementCurrentGroup(); Undo.RecordObject(polygon, "Add Collider Hole");
        polygon.holes.Add(new ColliderPath2D { isHole = true, points =
            [center - extent, center + new Vector2(extent.x, -extent.y), center + extent, center + new Vector2(-extent.x, extent.y)] });
        EditorUtility.SetDirty(polygon); SceneView.RepaintAll();
    }
}
[CustomEditor(typeof(Rigidbody2D))]
public sealed class Rigidbody2DEditor : BEngine.Editor.Editor
{
    public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var body = (Rigidbody2D)target;
        try { GUILayout.Label($"Mass: {body.mass:0.###}   Inertia: {body.inertia:0.###}   Center: {body.centerOfMass}", EditorStyles.miniLabel); }
        catch (InvalidDataException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        if (!EditorApplication.isPlaying) return;
        GUILayout.Label(body.IsSleeping ? "Sleeping" : "Awake", EditorStyles.miniLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Sleep")) body.Sleep();
        if (GUILayout.Button("Wake Up")) body.WakeUp();
        GUILayout.EndHorizontal();
    }
}
