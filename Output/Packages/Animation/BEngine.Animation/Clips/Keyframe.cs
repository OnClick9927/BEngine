namespace BEngine.Animation;

public readonly struct Keyframe : IEquatable<Keyframe>
{
    public Fix64 time { get; init; }
    public Fix64 value { get; init; }
    public Fix64 inTangent { get; init; }
    public Fix64 outTangent { get; init; }
    public Keyframe(Fix64 time, Fix64 value) { this.time = time; this.value = value; inTangent = 0; outTangent = 0; }
    public Keyframe(Fix64 time, Fix64 value, Fix64 inTangent, Fix64 outTangent)
    { this.time = time; this.value = value; this.inTangent = inTangent; this.outTangent = outTangent; }
    public bool Equals(Keyframe other) => time == other.time && value == other.value && inTangent == other.inTangent && outTangent == other.outTangent;
    public override bool Equals(object? obj) => obj is Keyframe other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(time, value, inTangent, outTangent);
}
