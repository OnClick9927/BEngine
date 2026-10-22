using BEngine.Editor;

namespace BEngine.Video.Editor;

[CustomEditor(typeof(VideoClip))]
public sealed class VideoClipEditor : BAssetEditor
{
    private VideoPreviewSession? _preview;
    protected override void OnEnable() { base.OnEnable(); _preview = new VideoPreviewSession(); }
    protected override void OnDisable() { _preview?.Dispose(); _preview = null; base.OnDisable(); }
    public override bool RequiresConstantRepaint() => _preview?.HasPendingWork == true;
    public override void OnInspectorGUI()
    {
        if (target is not VideoClip { isDestroyed: false } clip) return;
        DrawAssetInformationHeader(clip);
        DrawVideoImportSettings(); DrawApplyBar();
        _preview?.Draw(clip);
    }

    private void DrawVideoImportSettings()
    {
        if (assetImporter is not VideoImporter importer) { DrawImportSettings(); return; }
        using var fields = new SerializedObject(importer);
        using var disabled = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        GUILayout.Label("Import Settings", EditorStyles.boldLabel);
        Draw(nameof(VideoImporter.importMode));
        if (importer.importMode == VideoImportMode.PortableFrames)
        { Draw(nameof(VideoImporter.maxWidth)); Draw(nameof(VideoImporter.frameRate)); Draw(nameof(VideoImporter.importAudio)); }
        else EditorGUILayout.HelpBox("Preserves the original container and uses the target platform's decoder. Check decoder and audio support for your target; Portable Frames provides the broadest compatibility.", MessageType.Info);
        Draw(nameof(AssetImporter.userData));
        if (fields.ApplyModifiedProperties()) hasUnsavedChanges = true;
        void Draw(string name) { if (fields.FindProperty(name) is { } property) EditorGUILayout.PropertyField(property); }
    }
}
