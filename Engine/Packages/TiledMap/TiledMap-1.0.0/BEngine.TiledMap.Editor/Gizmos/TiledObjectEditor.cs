using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

[CustomEditor(typeof(TiledObject))]
public sealed class TiledObjectEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        var item = (TiledObject)target;
        GUILayout.Label($"Tiled object #{item.objectId} · {item.className}", EditorStyles.boldLabel);
        DrawDefaultInspector();
        if (item.shape != TiledObjectShape.Point && item.GetComponent<BEngine.Physics2D.Collider2D>() is null && GUILayout.Button("Create Physics Collider"))
        {
            if (TiledObjectColliderUtility.Create(item) is { } collider)
            {
                Undo.RegisterCreatedObjectUndo(collider, "Create Tiled Object Collider");
                if (item.gameObject.scene is { } scene) EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawObject(TiledObject item, GizmoType type)
    {
        if (!item.enabled || !item.gameObject.activeInHierarchy) return;
        Gizmos.color = new Color((Fix64).3, 1, (Fix64).6);
        var outline = item.GetLocalOutline();
        if (outline.Count == 1)
        {
            var center = item.transform.position;
            Gizmos.DrawLine(center - Vector2.right * (Fix64).1, center + Vector2.right * (Fix64).1);
            Gizmos.DrawLine(center - Vector2.up * (Fix64).1, center + Vector2.up * (Fix64).1);
            return;
        }
        var count = item.shape == TiledObjectShape.Polyline ? outline.Count - 1 : outline.Count;
        for (var index = 0; index < count; index++)
            Gizmos.DrawLine(item.transform.TransformPoint(outline[index]), item.transform.TransformPoint(outline[(index + 1) % outline.Count]));
    }
}
