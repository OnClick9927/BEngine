namespace BEngine;

public enum AnimationTangentMode { Free, Linear, Constant, Auto, ClampedAuto }
[Flags]
public enum WeightedMode { None = 0, In = 1, Out = 2, Both = In | Out }

public readonly struct Keyframe : IEquatable<Keyframe>
{
    public Fix64 time { get; init; }
    public Fix64 value { get; init; }
    public Fix64 inTangent { get; init; }
    public Fix64 outTangent { get; init; }
    public AnimationTangentMode inMode { get; init; }
    public AnimationTangentMode outMode { get; init; }
    public WeightedMode weightedMode { get; init; }
    public Fix64 inWeight { get; init; } = Fix64.One / 3;
    public Fix64 outWeight { get; init; } = Fix64.One / 3;
    public Keyframe(Fix64 time, Fix64 value) { this.time = time; this.value = value; inTangent = 0; outTangent = 0; }
    public Keyframe(Fix64 time, Fix64 value, Fix64 inTangent, Fix64 outTangent)
    { this.time = time; this.value = value; this.inTangent = inTangent; this.outTangent = outTangent; }
    public bool Equals(Keyframe other) => time == other.time && value == other.value && inTangent == other.inTangent && outTangent == other.outTangent &&
        inMode == other.inMode && outMode == other.outMode && weightedMode == other.weightedMode && inWeight == other.inWeight && outWeight == other.outWeight;
    public override bool Equals(object? obj) => obj is Keyframe other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(HashCode.Combine(time, value, inTangent, outTangent), inMode, outMode, weightedMode, inWeight, outWeight);
}
