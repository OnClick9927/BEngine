using BEngine.Editor;

namespace BEngine.Cinemachine.Editor;

[CustomEditor(typeof(CinemachineCamera))]
public sealed class CinemachineCameraEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var camera = (CinemachineCamera)target;
        var scene = camera.gameObject.scene;
        var output = scene?.QueryComponents<Camera2D>().FirstOrDefault(item => item.isMain)
            ?? scene?.QueryComponents<Camera2D>().FirstOrDefault();
        using var disabled = new EditorGUI.DisabledScope(output is null || EditorApplication.isPlaying);
        if (GUILayout.Button("Match Output Camera") && output is not null)
        {
            Undo.RecordObjects([camera, camera.transform], "Match Cinemachine Camera");
            camera.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);
            camera.orthographicSize = output.size;
            EditorUtility.SetDirty(camera); SceneView.RepaintAll();
        }
        if (GUILayout.Button("Use as Highest Priority") && scene is not null)
        {
            var maximum = scene.QueryComponents<CinemachineCamera>().Where(item => item != camera).Select(item => item.priority).DefaultIfEmpty(0).Max();
            Undo.RecordObject(camera, "Cinemachine Priority");
            camera.priority = maximum == int.MaxValue ? maximum : maximum + 1;
            EditorUtility.SetDirty(camera);
        }
        if (camera.follow is null) GUILayout.Label("Assign Follow for tracking; otherwise the camera uses its Transform.", EditorStyles.wordWrappedMiniLabel);
    }
    [DrawGizmo(GizmoType.Selected)]
    private static void DrawFraming(CinemachineCamera camera, GizmoType type)
    {
        if (camera.isDestroyed) return;
        Gizmos.color = new Color(0, 1, 1);
        var center = camera.follow is { isDestroyed: false } follow ? follow.position + follow.TransformDirection(camera.trackedObjectOffset) : camera.transform.position;
        var half = new Vector2(camera.orthographicSize * (Fix64)16 / 9, camera.orthographicSize);
        Gizmos.DrawLine(center - half, center + new Vector2(half.x, -half.y));
        Gizmos.DrawLine(center + new Vector2(half.x, -half.y), center + half);
        Gizmos.DrawLine(center + half, center + new Vector2(-half.x, half.y));
        Gizmos.DrawLine(center + new Vector2(-half.x, half.y), center - half);
        if (camera.follow is { isDestroyed: false } target) Gizmos.DrawLine(center, target.position);
    }
}
[CustomEditor(typeof(CinemachineBrain))]
public sealed class CinemachineBrainEditor : BEngine.Editor.Editor
{
    public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var brain = (CinemachineBrain)target;
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.ObjectField("Live Camera", brain.activeVirtualCamera, true);
        EditorGUILayout.Toggle("Blending", brain.activeBlend is not null);
    }
}
