namespace BEngine;

public readonly record struct Particle2D(
    Vector2 Position,
    Vector2 Velocity,
    Fix64 Rotation,
    Fix64 AngularVelocity,
    Vector2 Size,
    Color Color,
    Fix64 RemainingLifetime);
