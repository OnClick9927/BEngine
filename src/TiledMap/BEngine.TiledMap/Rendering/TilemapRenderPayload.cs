namespace BEngine.TiledMap;

internal readonly record struct TilemapRenderPayload(
    Vector2 Center,
    Fix64 Rotation,
    Vector2 Size,
    Color Color,
    Rect Uv,
    bool FlipX,
    bool FlipY,
    string Texture);
