namespace BEngine;

internal readonly record struct GizmoLine2D(
    Vector2 From,
    Vector2 To,
    Color Color,
    Fix64 LineWidth);
