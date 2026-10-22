namespace BEngine.TiledMap;

internal readonly record struct TilemapRenderPayload(
    Vector2 Center,
    Vector2 AxisX,
    Vector2 AxisY,
    Color Color,
    Rect Uv,
    bool FlipX,
    bool FlipY,
    string Texture,
    TilemapLayout Layout = TilemapLayout.Rectangle,
    Fix64? HexSideRatio = null);
