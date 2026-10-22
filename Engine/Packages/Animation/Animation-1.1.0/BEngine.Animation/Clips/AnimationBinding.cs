using BEngine.Serialization;

namespace BEngine;

public sealed class AnimationBinding
{
    public string relativePath { get; set; } = string.Empty;
    public string componentType { get; set; } = typeof(Transform).FullName!;
    public string propertyName { get; set; } = string.Empty;
    public AnimationCurve curve { get; set; } = new();
    public AnimationBindingKind kind { get; set; }
    public string objectType { get; set; } = typeof(BObject).FullName!;
    // Object keys use stable slots so timeline time edits share the same curve operations.
    [HideInInspector]
    public List<BObject?> objectReferences { get; set; } = [];

    public ObjectReferenceKeyframe[] GetObjectReferenceKeys() => kind != AnimationBindingKind.ObjectReference ? [] :
        curve.keys.Select(key => new ObjectReferenceKeyframe(key.time, ObjectAt(key.value))).ToArray();

    public void SetObjectReferenceKeys(IEnumerable<ObjectReferenceKeyframe> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var ordered = keys.ToArray();
        if (ordered.Any(key => key.value is not null and not BAsset and not Sprite))
            throw new ArgumentException("Animation object keys must reference assets or sprites, not scene objects.", nameof(keys));
        var expected = RuntimeTypeCache.FindType(objectType);
        if (expected is not null && ordered.Any(key => key.value is not null && !expected.IsInstanceOfType(key.value)))
            throw new ArgumentException("Object key type does not match the animated property.", nameof(keys));
        kind = AnimationBindingKind.ObjectReference;
        objectReferences.Clear(); curve.keys = [];
        foreach (var key in ordered) SetObjectReferenceKey(key.time, key.value);
    }

    public void SetObjectReferenceKey(Fix64 time, BObject? value)
    {
        if (value is not null and not BAsset and not Sprite)
            throw new ArgumentException("Animation object keys must reference assets or sprites, not scene objects.", nameof(value));
        var expected = RuntimeTypeCache.FindType(objectType);
        if (value is not null && expected is not null && !expected.IsInstanceOfType(value))
            throw new ArgumentException("Object key type does not match the animated property.", nameof(value));
        kind = AnimationBindingKind.ObjectReference;
        var slot = objectReferences.FindIndex(item => ReferenceEquals(item, value));
        if (slot < 0) { slot = objectReferences.Count; objectReferences.Add(value); }
        curve.AddKey(new(time, slot) { inMode = AnimationTangentMode.Constant, outMode = AnimationTangentMode.Constant });
    }

    internal BObject? ObjectAt(Fix64 index)
    {
        var slot = (int)index;
        if ((Fix64)slot != index || (uint)slot >= (uint)objectReferences.Count)
            throw new InvalidDataException("Invalid object-reference animation key slot.");
        return objectReferences[slot] is { isDestroyed: false } value ? value : null;
    }

    internal AnimationValue Evaluate(Fix64 time, AnimationBindingKind? propertyKind = null) => (propertyKind ?? kind) switch
    {
        AnimationBindingKind.ObjectReference => new(0, curve.length == 0 ? null : ObjectAt(curve.EvaluateDiscrete(time))),
        AnimationBindingKind.Boolean => new(curve.EvaluateDiscrete(time) == 0 ? Fix64.Zero : Fix64.One),
        _ => new(curve.Evaluate(time))
    };

    internal void SetValue(Fix64 time, AnimationValue value)
    {
        if (kind == AnimationBindingKind.ObjectReference) SetObjectReferenceKey(time, value.Reference);
        else
        {
            var keys = curve.keys;
            var index = Array.FindIndex(keys, key => key.time == time);
            var existing = index >= 0 ? keys[index] : new Keyframe(time, value.Number);
            var key = existing with { time = time, value = value.Number };
            if (kind == AnimationBindingKind.Boolean) key = key with { inMode = AnimationTangentMode.Constant, outMode = AnimationTangentMode.Constant };
            curve.AddKey(key);
        }
    }
}
