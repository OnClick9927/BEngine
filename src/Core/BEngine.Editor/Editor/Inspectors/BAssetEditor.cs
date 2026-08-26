namespace BEngine.Editor;

[CustomEditor(typeof(BAsset), true)]
public class BAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (DrawDefaultInspector()) hasUnsavedChanges = true;
        DrawApplyBar();
    }

    public override void SaveChanges()
    {
        if (target is BAsset asset && AssetDatabase.SaveAsset(asset))
            hasUnsavedChanges = false;
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        if (target is BAsset asset && AssetDatabase.RevertAsset(asset))
            hasUnsavedChanges = false;
        base.DiscardChanges();
    }

    protected void DrawApplyBar()
    {
        if (target is not BAsset asset || !AssetDatabase.Contains(asset)) return;
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(!hasUnsavedChanges || !EditorAssetWritePolicy.CanWrite))
        {
            if (GUILayout.Button("Revert", GUILayout.Width(72))) DiscardChanges();
            if (GUILayout.Button("Apply", GUILayout.Width(72))) SaveChanges();
        }
        GUILayout.EndHorizontal();
    }
}
