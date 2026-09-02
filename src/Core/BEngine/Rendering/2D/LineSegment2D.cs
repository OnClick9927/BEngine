namespace BEngine;

internal readonly record struct LineSegment2D(
    Vector2 Center,
    Fix64 Rotation,
    Vector2 Size,
    Color Color);
