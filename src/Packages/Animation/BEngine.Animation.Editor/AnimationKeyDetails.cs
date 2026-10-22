namespace BEngine.Editor;

public sealed partial class AnimationWindow
{
    private void DrawKeyDetails(Rect area, AnimationClip clip, AnimationKeySelection selected, Keyframe key)
    {
        using var readOnly = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        var binding = clip.bindings[selected.Binding];
        var cell = area.width / 4;
        var frame = EditorGUI.IntField(new Rect(area.x, area.y, cell - 3, 22), "Frame", Mathf.RoundToInt(key.time * AnimationTimelineEditing.Rate(clip)));
        var value = key.value;
        if (binding.kind == AnimationBindingKind.ObjectReference)
        {
            var reference = binding.ObjectAt(key.value);
            var changed = EditorGUI.ObjectField(new Rect(area.x + cell, area.y, cell * 3 - 3, 22), reference,
                RuntimeTypeCache.FindType(binding.objectType) ?? typeof(BObject), false);
            if (!ReferenceEquals(reference, changed)) controller.SetObjectReferenceValue(selected.Binding, key.time, changed);
        }
        else if (binding.kind == AnimationBindingKind.Boolean)
            value = GUI.Toggle(new Rect(area.x + cell, area.y, cell - 3, 22), key.value != 0, "Value") ? 1 : 0;
        else value = (Fix64)TimelineFloatField(new Rect(area.x + cell, area.y, cell - 3, 22), "Value", (float)key.value);
        var input = key.inTangent; var output = key.outTangent;
        if (binding.kind == AnimationBindingKind.Numeric)
        {
            input = (Fix64)TimelineFloatField(new Rect(area.x + cell * 2, area.y, cell - 3, 22), "In", (float)key.inTangent);
            output = (Fix64)TimelineFloatField(new Rect(area.x + cell * 3, area.y, cell - 3, 22), "Out", (float)key.outTangent);
            var inMode = (AnimationTangentMode)EditorGUI.EnumPopup(new Rect(area.x, area.y + 26, cell * 2 - 3, 22), "In mode", key.inMode);
            var outMode = (AnimationTangentMode)EditorGUI.EnumPopup(new Rect(area.x + cell * 2, area.y + 26, cell * 2 - 3, 22), "Out mode", key.outMode);
            var weighted = (WeightedMode)EditorGUI.EnumPopup(new Rect(area.x, area.y + 52, cell * 2 - 3, 22), "Weights", key.weightedMode);
            var inWeight = TimelineFloatField(new Rect(area.x + cell * 2, area.y + 52, cell - 3, 22), "In", (float)key.inWeight);
            var outWeight = TimelineFloatField(new Rect(area.x + cell * 3, area.y + 52, cell - 3, 22), "Out", (float)key.outWeight);
            if (inMode != key.inMode || outMode != key.outMode || weighted != key.weightedMode || inWeight != (float)key.inWeight || outWeight != (float)key.outWeight)
                controller.EditSelectedTangentModes(inMode, outMode, weighted, (Fix64)inWeight, (Fix64)outWeight);
        }
        // Compare the displayed float values to avoid rounding fixed-point data on idle repaint.
        if (frame != Mathf.RoundToInt(key.time * AnimationTimelineEditing.Rate(clip)) || (float)value != (float)key.value ||
            (float)input != (float)key.inTangent || (float)output != (float)key.outTangent)
            controller.EditSelectedKey((Fix64)frame / AnimationTimelineEditing.Rate(clip),
                (float)value == (float)key.value ? key.value : value,
                (float)input == (float)key.inTangent ? key.inTangent : input,
                (float)output == (float)key.outTangent ? key.outTangent : output);
    }
}
