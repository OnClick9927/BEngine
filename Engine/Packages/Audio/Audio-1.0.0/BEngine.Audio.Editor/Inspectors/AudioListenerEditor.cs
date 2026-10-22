using BEngine.Editor;

namespace BEngine.Audio.Editor;

[CustomEditor(typeof(AudioListener))]
public sealed class AudioListenerEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        AudioListener.volume = (Fix64)EditorGUILayout.Slider(
            "Global Volume", (float)AudioListener.volume, 0, 1);
        AudioListener.pause = EditorGUILayout.Toggle("Global Pause", AudioListener.pause);
        EditorGUILayout.HelpBox("BEngine Audio is 2D. Stereo placement is controlled by AudioSource.panStereo.");
    }
}
