using BEngine.Editor;

namespace BEngine.Audio.Editor;

[CustomEditor(typeof(AudioClip))]
public sealed class AudioClipEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var clip = (AudioClip)target;
        DrawAssetInformationHeader(clip);
        DrawReadOnly("Channels", clip.channels.ToString());
        DrawReadOnly("Frequency", $"{clip.frequency} Hz");
        DrawReadOnly("Samples", clip.samples.ToString());
        DrawReadOnly("Length", $"{(double)clip.length:0.###} s");
        DrawReadOnly("Load State", clip.loadState.ToString());
        DrawImportSettings();
        DrawApplyBar();
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
