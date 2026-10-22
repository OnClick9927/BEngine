using BEngine.Editor;

namespace BEngine.Video.Editor;

[CustomEditor(typeof(VideoPlayer))]
public sealed class VideoPlayerEditor : BEngine.Editor.Editor
{
    private VideoPreviewSession? _preview;
    protected override void OnEnable() => _preview = new VideoPreviewSession();
    protected override void OnDisable() { _preview?.Dispose(); _preview = null; }
    public override bool RequiresConstantRepaint() => _preview?.HasPendingWork == true;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var player = (VideoPlayer)target;
        if (!EditorApplication.isPlaying) { _preview?.Draw(player.clip); return; }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Play")) player.Play();
        if (GUILayout.Button("Pause")) player.Pause();
        if (GUILayout.Button("Stop")) player.Stop();
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Next Frame")) player.StepForward();
        if (player.preparedClip is { } clip)
        {
            var next = EditorGUILayout.Slider("Time (seconds)", (float)player.time, 0, (float)clip.length);
            if (Math.Abs(next - (float)player.time) > 0.001f) player.time = (Fix64)next;
            GUILayout.Label($"{clip.width} × {clip.height} · {clip.frameRateExact:0.###} fps · {clip.frameCount} frames · Audio {(clip.audioClip is null ? "No" : "Yes")}", EditorStyles.wordWrappedLabel);
            GUILayout.Label($"{player.decoderBackend} · GPU frames {player.hardwareDecodedFrames}", EditorStyles.miniLabel);
        }
        if (player.error.Length > 0) GUILayout.Label(player.error, EditorStyles.wordWrappedLabel);
    }
}
