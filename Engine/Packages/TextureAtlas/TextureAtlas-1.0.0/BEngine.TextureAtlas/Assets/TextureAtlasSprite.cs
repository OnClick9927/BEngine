using YamlDotNet.Serialization;

namespace BEngine;

public sealed class TextureAtlasSprite
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Fix64 PivotX { get; set; } = Fix64.Half;
    public Fix64 PivotY { get; set; } = Fix64.Half;

    [YamlIgnore]
    public Vector2 pivot => new(PivotX, PivotY);

    public Rect NormalizedUv(int atlasWidth, int atlasHeight)
    {
        if (atlasWidth <= 0 || atlasHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        return new Rect(
        (Fix64)X / atlasWidth,
        (Fix64)Y / atlasHeight,
        (Fix64)Width / atlasWidth,
        (Fix64)Height / atlasHeight);
    }

    internal void Validate(int atlasWidth, int atlasHeight)
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("Texture atlas sprite names cannot be empty.");
        if (Width <= 0 || Height <= 0 || X < 0 || Y < 0 ||
            (long)X + Width > atlasWidth || (long)Y + Height > atlasHeight)
            throw new InvalidDataException($"Texture atlas sprite '{Name}' is outside the atlas bounds.");
        if (PivotX < 0 || PivotX > 1 || PivotY < 0 || PivotY > 1)
            throw new InvalidDataException($"Texture atlas sprite '{Name}' has an invalid pivot.");
        Source = Source.Replace('\\', '/').Trim();
        Name = Name.Trim();
    }
}
