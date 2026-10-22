using BEngine.Editor;

namespace BEngine.Audio.Editor;

[CustomEditor(typeof(AudioClip))]
public sealed class AudioClipEditor : BAssetEditor
{
    private AudioPreviewSession? _preview;
    protected override void OnEnable() { base.OnEnable(); _preview = new AudioPreviewSession(); }
    protected override void OnDisable() { _preview?.Dispose(); _preview = null; base.OnDisable(); }
    public override bool RequiresConstantRepaint() => _preview?.HasPendingWork == true;

    public override void OnInspectorGUI()
    {
        var clip = (AudioClip)target;
        DrawAssetInformationHeader(clip);
        DrawReadOnly("Channels", clip.channels.ToString());
        DrawReadOnly("Frequency", $"{clip.frequency} Hz");
        DrawReadOnly("Samples", clip.samples.ToString());
        DrawReadOnly("Length", $"{(double)clip.length:0.###} s");
        DrawReadOnly("Load State", clip.loadState.ToString());
        DrawReadOnly("Load Type", clip.loadType.ToString());
        DrawReadOnly("Decoded Memory", $"{clip.decodedMemoryBytes:N0} bytes");
        DrawReadOnly("Encoded Memory", $"{clip.encodedMemoryBytes:N0} bytes");
        if (clip.loadError.Length > 0) EditorGUILayout.HelpBox(clip.loadError, MessageType.Error);
        DrawImportSettings();
        DrawApplyBar();
        _preview?.DrawControls(clip);
    }

    public override string GetInfoString()
    {
        var clip = (AudioClip)target;
        return $"{clip.channels} ch | {clip.frequency} Hz | {(double)clip.length:0.###} s";
    }

    private static void DrawReadOnly(string label, string value)
    {
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.TextField(label, value);
    }
}
