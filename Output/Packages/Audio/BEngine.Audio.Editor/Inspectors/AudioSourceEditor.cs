using BEngine.Editor;

namespace BEngine.Audio.Editor;

[CustomEditor(typeof(AudioSource))]
public sealed class AudioSourceEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        var source = (AudioSource)target;
        EditorGUI.BeginChangeCheck();
        var clip = EditorGUILayout.ObjectField("Audio Clip", source.clip, typeof(AudioClip), false) as AudioClip;
        var playOnAwake = EditorGUILayout.Toggle("Play On Awake", source.playOnAwake);
        var loop = EditorGUILayout.Toggle("Loop", source.loop);
        var mute = EditorGUILayout.Toggle("Mute", source.mute);
        var volume = (Fix64)EditorGUILayout.Slider("Volume", (float)source.volume, 0, 1);
        var pitch = (Fix64)EditorGUILayout.Slider("Pitch", (float)source.pitch, 0.01f, 3);
        var pan = (Fix64)EditorGUILayout.Slider("Stereo Pan", (float)source.panStereo, -1, 1);
        var priority = EditorGUILayout.IntSlider("Priority", source.priority, 0, 256);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(source, "Edit Audio Source");
            source.clip = clip;
            source.playOnAwake = playOnAwake;
            source.loop = loop;
            source.mute = mute;
            source.volume = volume;
            source.pitch = pitch;
            source.panStereo = pan;
            source.priority = priority;
            EditorUtility.SetDirty(source);
        }

        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.Toggle("Playing", source.isPlaying);
        EditorGUILayout.FloatField("Time", (float)source.time);
    }
}
