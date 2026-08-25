namespace BEngine;

internal readonly record struct SpriteRenderData2D(
    string Texture,
    string BatchIdentity,
    Rect Uv,
    Vector2 Pivot,
    bool IsTextured)
{
    internal static SpriteRenderData2D Solid => new(
        string.Empty, string.Empty, new Rect(0, 0, 1, 1),
        new Vector2(Fix64.Half, Fix64.Half), false);
}
