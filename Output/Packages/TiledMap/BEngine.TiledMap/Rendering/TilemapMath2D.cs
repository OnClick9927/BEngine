namespace BEngine.TiledMap;

internal static class TilemapMath2D
{
    internal static Vector2 Rotate(Vector2 value, Fix64 degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var cosine = Mathf.Cos(radians);
        var sine = Mathf.Sin(radians);
        return new Vector2(value.x * cosine - value.y * sine,
            value.x * sine + value.y * cosine);
    }
}
