namespace BEngine;

public readonly record struct Touch(
    int fingerId,
    Vector2 position,
    Vector2 deltaPosition,
    Fix64 deltaTime,
    int tapCount,
    TouchPhase phase,
    Fix64 pressure = default,
    Fix64 maximumPossiblePressure = default,
    Fix64 radius = default,
    Fix64 radiusVariance = default)
{
    public Vector2 rawPosition => position;
}
