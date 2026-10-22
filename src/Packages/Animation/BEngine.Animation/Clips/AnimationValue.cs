namespace BEngine;

public enum AnimationBindingKind { Numeric, Boolean, ObjectReference }

public readonly record struct ObjectReferenceKeyframe(Fix64 time, BObject? value);

internal readonly record struct AnimationValue(Fix64 Number, BObject? Reference = null)
{
    internal static AnimationValue Mix(AnimationValue from, AnimationValue to, Fix64 weight, AnimationBindingKind kind) =>
        kind == AnimationBindingKind.Numeric ? new(Mathf.Lerp(from.Number, to.Number, weight)) : weight >= Fix64.One / 2 ? to : from;
}
