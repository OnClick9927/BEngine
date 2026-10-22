namespace BEngine;

internal sealed record SpriteShapeRenderPayload(
    Vector2[] Vertices,
    Vector2[] Uv,
    int[] Triangles,
    Color Color,
    string Texture);
