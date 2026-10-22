namespace BEngine;

public readonly record struct Particle2D(
    Vector2 Position,
    Vector2 Velocity,
    Fix64 Rotation,
    Fix64 AngularVelocity,
    Vector2 Size,
    Color Color,
    Fix64 RemainingLifetime)
{
    /// <summary>Total lifetime at spawn.  Keeping this on the particle makes
    /// editor preview and lifetime modules deterministic without hidden state.
    /// </summary>
    public Fix64 Lifetime { get; init; } = RemainingLifetime;
    public Vector2 InitialSize { get; init; } = Size;
    public Color InitialColor { get; init; } = Color;
    public ulong Id { get; init; }
    public Fix64 NormalizedAge => Lifetime <= Fix64.Epsilon
        ? Fix64.One : Mathf.Clamp01((Lifetime - RemainingLifetime) / Lifetime);
}
