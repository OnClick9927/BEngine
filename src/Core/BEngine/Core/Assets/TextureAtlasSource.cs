namespace BEngine;

public sealed class TextureAtlasSource
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 0.5f;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
            throw new InvalidDataException("Texture atlas source paths cannot be empty.");
        if (string.IsNullOrWhiteSpace(Name))
            Name = System.IO.Path.GetFileNameWithoutExtension(Path);
        if (!float.IsFinite(PivotX) || !float.IsFinite(PivotY) ||
            PivotX < 0 || PivotX > 1 || PivotY < 0 || PivotY > 1)
            throw new InvalidDataException($"Texture atlas source '{Name}' has an invalid pivot.");
        Path = Path.Replace('\\', '/').Trim();
        Name = Name.Trim();
    }
}
