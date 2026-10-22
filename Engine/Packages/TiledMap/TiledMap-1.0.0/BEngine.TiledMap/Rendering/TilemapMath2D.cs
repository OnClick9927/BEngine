namespace BEngine.TiledMap;

internal static class TilemapMath2D
{
    internal static Vector2 Rotate(Vector2 value, Fix64 degrees)
    {
        var quarter = degrees % 360;
        if (quarter < 0) quarter += 360;
        if (quarter == 0) return value;
        if (quarter == 90) return new(-value.y, value.x);
        if (quarter == 180) return -value;
        if (quarter == 270) return new(value.y, -value.x);
        var radians = degrees * Mathf.Deg2Rad;
        var cosine = Mathf.Cos(radians);
        var sine = Mathf.Sin(radians);
        return new Vector2(value.x * cosine - value.y * sine,
            value.x * sine + value.y * cosine);
    }
}
