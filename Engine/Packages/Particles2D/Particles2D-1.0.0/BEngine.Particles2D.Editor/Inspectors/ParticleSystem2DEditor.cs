namespace BEngine.Editor;

[CustomEditor(typeof(ParticleSystem2D))]
public sealed class ParticleSystem2DEditor : Editor
{
    private readonly ParticleEditorPreview _preview = new();
    public void Preview(Fix64 time) => _preview.Sample((ParticleSystem2D)target, time);
    public void StopPreview() => _preview.Dispose();
    protected override void OnDisable() => StopPreview();
    public override bool RequiresConstantRepaint() =>
        !EditorApplication.isPlaying && ((ParticleSystem2D)target).editorPreview;

    public override void OnInspectorGUI()
    {
        var system = (ParticleSystem2D)target;
        DrawDefaultInspector();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GUILayout.Label($"Particles: {system.particles.Count}/{system.maxParticles}", EditorStyles.miniLabel);
            return;
        }
        GUILayout.Space(8);
        GUILayout.Label("Edit Mode Preview", EditorStyles.boldLabel);
        var preview = EditorGUILayout.Toggle("Preview", system.editorPreview);
        if (preview != system.editorPreview)
        {
            system.editorPreview = preview;
            if (!preview) StopPreview();
        }
        using (new EditorGUI.DisabledScope(!system.editorPreview || EditorApplication.isPlaying))
        {
            var length = Math.Clamp((float)system.duration * (system.loop ? 3 : 1), 0.01f, 3600);
            var time = EditorGUILayout.Slider("Time", (float)system.editorPreviewTime, 0, length);
            if (time != (float)system.editorPreviewTime || system.editorPreview)
            {
                system.editorPreviewTime = (Fix64)time;
                Preview(system.editorPreviewTime);
            }
            if (GUILayout.Button("Restart Preview"))
            {
                system.editorPreviewTime = Fix64.Zero;
                Preview(0);
            }
        }
        GUILayout.Label($"Particles: {system.particles.Count}/{system.maxParticles}", EditorStyles.miniLabel);
    }
}
