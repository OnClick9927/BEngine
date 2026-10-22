namespace BEngine.Editor;

[CustomEditor(typeof(SpritePoseRetarget2D))]
public sealed class SpritePoseRetarget2DEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Map By Name")) MapByName();
            if (GUILayout.Button("Capture Reference Pose")) CaptureReferencePose();
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox("Capture both rigs in their reference poses, then animate the source. Rotation/translation/scale deltas are applied during LateUpdate.");
    }

    public int MapByName() => Edit("Map retarget bones", rig => rig.MapByName());
    public int CaptureReferencePose() => Edit("Capture retarget reference pose", rig => rig.CaptureReferencePose());

    private int Edit(string label, Func<SpritePoseRetarget2D, int> action)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        var rig = (SpritePoseRetarget2D)target;
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(rig, label);
        var count = action(rig); EditorUtility.SetDirty(rig);
        if (rig.gameObject.scene is { } scene) EditorSceneManager.MarkSceneDirty(scene);
        return count;
    }
}
