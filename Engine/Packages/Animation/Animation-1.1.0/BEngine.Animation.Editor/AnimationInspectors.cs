namespace BEngine.Editor;

[CustomEditor(typeof(AnimationClip))]
public sealed class AnimationClipEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var clip = (AnimationClip)target;
        DrawAssetInformationHeader(clip);
        GUILayout.Label($"{clip.bindings.Count} curves · {clip.events.Count} events · {(double)clip.length:0.###} s");
        if (GUILayout.Button("Open Animation")) AnimationWindow.Open(clip);
        DrawImportSettings(); DrawApplyBar();
    }
}
[CustomEditor(typeof(AnimatorController))]
public sealed class AnimatorControllerEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var asset = (AnimatorController)target;
        DrawAssetInformationHeader(asset);
        GUILayout.Label($"{asset.states.Count} states · {asset.parameters.Count} parameters · Entry: {asset.defaultState}");
        if (GUILayout.Button("Open Animator")) AnimatorWindow.Open(asset);
        DrawImportSettings(); DrawApplyBar();
    }
}
[CustomEditor(typeof(Animation))]
public sealed class AnimationComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (target is Animation { clip: { } clip } && GUILayout.Button("Edit Animation Clip")) AnimationWindow.Open(clip);
    }
}
[CustomEditor(typeof(Animator))]
public sealed class AnimatorComponentEditor : Editor
{
    public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var animator = (Animator)target;
        if (animator.runtimeAnimatorController is not { } asset) return;
        if (GUILayout.Button("Edit Animator Controller")) AnimatorWindow.Open(asset);
        if (!EditorApplication.isPlaying) return;
        GUILayout.Label($"State: {animator.currentStateName} · Time: {(double)animator.normalizedTime:0.##}");
        foreach (var parameter in asset.parameters)
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(parameter.name, (Fix64)EditorGUILayout.FloatField(parameter.name, (float)animator.GetFloat(parameter.name))); break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(parameter.name, EditorGUILayout.IntField(parameter.name, animator.GetInteger(parameter.name))); break;
                case AnimatorControllerParameterType.Bool:
                    animator.SetBool(parameter.name, EditorGUILayout.Toggle(parameter.name, animator.GetBool(parameter.name))); break;
                case AnimatorControllerParameterType.Trigger:
                    if (GUILayout.Button(parameter.name)) animator.SetTrigger(parameter.name); break;
            }
    }
}
