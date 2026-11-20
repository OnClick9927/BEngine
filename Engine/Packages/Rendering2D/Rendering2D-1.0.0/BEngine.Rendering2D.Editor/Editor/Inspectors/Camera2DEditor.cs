namespace BEngine.Editor;

[CustomEditor(typeof(Camera2D))]
public sealed class Camera2DEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var camera = (Camera2D)target;

        EditorGUI.BeginChangeCheck();
        var isMain = EditorGUILayout.Toggle("Main Camera", camera.isMain);
        var priority = EditorGUILayout.FloatField("Priority", (float)camera.priority);
        var clearMode = (CameraClearMode)EditorGUILayout.EnumPopup("Clear Mode", camera.clearMode);
        var backgroundColor = camera.backgroundColor;
        if (clearMode == CameraClearMode.Color)
            backgroundColor = EditorGUILayout.ColorField("Background", backgroundColor);
        var orthographicSize = (Fix64)EditorGUILayout.FloatField(
            "Orthographic Size", (float)camera.size);
        var viewportPosition = EditorGUILayout.Vector2Field("Viewport Position", camera.viewportRect.position);
        var viewportSize = EditorGUILayout.Vector2Field("Viewport Size", camera.viewportRect.size);
        var targetTexture = EditorGUILayout.ObjectField(
            "Target Texture", camera.targetTexture, typeof(RenderTexture), allowSceneObjects: false) as RenderTexture;
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(camera, "Edit Camera 2D");
            camera.isMain = isMain;
            camera.priority = (Fix64)priority;
            camera.clearMode = clearMode;
            camera.backgroundColor = backgroundColor;
            camera.size = orthographicSize;
            camera.viewportRect = new Rect(
                viewportPosition.x, viewportPosition.y, viewportSize.x, viewportSize.y);
            camera.targetTexture = targetTexture;
            EditorUtility.SetDirty(camera);
        }

        EditorGUI.BeginChangeCheck();
        var cullingMask = EditorGUILayout.LayerMaskField("Culling Mask", camera.cullingMask);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(camera, "Edit Camera Culling Mask");
            camera.cullingMask = cullingMask;
            EditorUtility.SetDirty(camera);
        }
    }
}
