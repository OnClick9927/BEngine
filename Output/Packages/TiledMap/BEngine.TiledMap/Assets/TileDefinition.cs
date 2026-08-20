using YamlDotNet.Serialization;

namespace BEngine.TiledMap;

public sealed class TileDefinition
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Tile 1";
    public string Texture { get; set; } = string.Empty;
    public float UvX { get; set; }
    public float UvY { get; set; }
    public float UvWidth { get; set; } = 1;
    public float UvHeight { get; set; } = 1;
    public byte Red { get; set; } = byte.MaxValue;
    public byte Green { get; set; } = byte.MaxValue;
    public byte Blue { get; set; } = byte.MaxValue;
    public byte Alpha { get; set; } = byte.MaxValue;
    public bool HasCollider { get; set; }

    [YamlIgnore]
    public Rect uv => new((Fix64)(double)UvX, (Fix64)(double)UvY,
        (Fix64)(double)UvWidth, (Fix64)(double)UvHeight);

    [YamlIgnore]
    public Color tint => new(
        (Fix64)Red / byte.MaxValue,
        (Fix64)Green / byte.MaxValue,
        (Fix64)Blue / byte.MaxValue,
        (Fix64)Alpha / byte.MaxValue);

    internal void Validate()
    {
        if (Id <= 0) throw new InvalidDataException("Tile IDs must be positive; zero is reserved for empty cells.");
        if (string.IsNullOrWhiteSpace(Name)) Name = $"Tile {Id}";
        if (!float.IsFinite(UvX) || !float.IsFinite(UvY) || !float.IsFinite(UvWidth) ||
            !float.IsFinite(UvHeight) || UvWidth <= 0 || UvHeight <= 0 ||
            UvX < 0 || UvY < 0 || UvX + UvWidth > 1.0001f || UvY + UvHeight > 1.0001f)
            throw new InvalidDataException($"Tile {Id} has invalid normalized UV coordinates.");
    }
}
