using BEngine.Editor;

namespace BEngine.PostProcessing.Editor;

[CustomEditor(typeof(PostProcessProfile))]
public sealed class PostProcessProfileEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var profile = (PostProcessProfile)target;
        DrawAssetInformationHeader(profile);
        var color = profile.colorAdjustments; var bloom = profile.bloom; var vignette = profile.vignette;
        var chromatic = profile.chromaticAberration; var grain = profile.filmGrain;
        var distortion = profile.lensDistortion; var lookup = profile.colorLookup;
        Active(profile, "Color Adjustments", color);
        Slider(profile, "Exposure", color.postExposure, -10, 10);
        Slider(profile, "Contrast", color.contrast, -100, 100);
        Slider(profile, "Hue Shift", color.hueShift, -180, 180);
        Slider(profile, "Saturation", color.saturation, -100, 100);
        Color(profile, "Color Filter", color.colorFilter);
        Active(profile, "Bloom", bloom);
        Slider(profile, "Intensity", bloom.intensity, 0, 10);
        Slider(profile, "Threshold", bloom.threshold, 0, 5);
        Slider(profile, "Scatter", bloom.scatter, 0, 1);
        Active(profile, "Vignette", vignette);
        Color(profile, "Color", vignette.color);
        Slider(profile, "Intensity", vignette.intensity, 0, 1);
        Slider(profile, "Smoothness", vignette.smoothness, 0, 1);
        Slider(profile, "Roundness", vignette.roundness, 0, 1);
        Active(profile, "Chromatic Aberration", chromatic);
        Slider(profile, "Intensity", chromatic.intensity, 0, 1);
        Active(profile, "Film Grain", grain);
        Slider(profile, "Intensity", grain.intensity, 0, 1);
        Slider(profile, "Response", grain.response, 0, 1);
        Active(profile, "Lens Distortion", distortion);
        Slider(profile, "Intensity", distortion.intensity, -1, 1);
        Slider(profile, "X Multiplier", distortion.xMultiplier, 0, 1);
        Slider(profile, "Y Multiplier", distortion.yMultiplier, 0, 1);
        Active(profile, "Color Lookup", lookup);
        var lookupTexture = EditorGUILayout.ObjectField("Texture", lookup.texture, false);
        if (lookupTexture != lookup.texture) Edit(profile, () => lookup.texture = lookupTexture as Texture);
        Slider(profile, "Contribution", lookup.contribution, 0, 1);
        if (GUILayout.Button("Save Profile")) AssetDatabase.SaveAsset(profile);
        DrawImportSettings(); DrawApplyBar();
    }
    private static void Active(PostProcessProfile profile, string label, PostProcessEffectSettings effect)
    {
        GUILayout.Space(6);
        var active = EditorGUILayout.Toggle(label, effect.active);
        if (active != effect.active) Edit(profile, () => effect.active = active);
    }
    private static void Slider(PostProcessProfile profile, string label, Fix64Parameter parameter, float minimum, float maximum)
    {
        GUILayout.BeginHorizontal();
        var enabled = EditorGUILayout.Toggle("Override", parameter.overrideState, GUILayout.Width(110));
        var value = (Fix64)EditorGUILayout.Slider(label, (float)parameter.value, minimum, maximum, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        if (enabled != parameter.overrideState || value != parameter.value) Edit(profile, () => { parameter.overrideState = enabled; parameter.value = value; });
    }
    private static void Color(PostProcessProfile profile, string label, ColorParameter parameter)
    {
        GUILayout.BeginHorizontal();
        var enabled = EditorGUILayout.Toggle("Override", parameter.overrideState, GUILayout.Width(110));
        var value = EditorGUILayout.ColorField(label, parameter.value, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        if (enabled != parameter.overrideState || !value.Equals(parameter.value)) Edit(profile, () => { parameter.overrideState = enabled; parameter.value = value; });
    }
    private static void Edit(PostProcessProfile profile, Action change)
    { Undo.RecordObject(profile, "Edit Post Process Profile"); change(); EditorUtility.SetDirty(profile); SceneView.RepaintAll(); }
}
[CustomEditor(typeof(PostProcessVolume))]
public sealed class PostProcessVolumeEditor : BEngine.Editor.Editor
{
    private BEngine.Editor.Editor? _profileEditor;
    private bool _expanded = true;
    protected override void OnDisable() { _profileEditor?.Dispose(); _profileEditor = null; }
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var volume = (PostProcessVolume)target;
        if (volume.profile is not { isDestroyed: false } profile) { OnDisable(); return; }
        _expanded = EditorGUILayout.Foldout(_expanded, "Profile Overrides (shared asset)");
        if (!_expanded) return;
        BEngine.Editor.Editor.CreateCachedEditor(profile, typeof(PostProcessProfileEditor), ref _profileEditor);
        _profileEditor!.OnInspectorGUI();
    }
}
