using BEngine.Serialization;
using YamlDotNet.Serialization;

namespace BEngine;

[CreateAssetMenu(fileName = "New Sprite", menuName = "2D/Sprite", order = 240)]
[EditorIcon("Icons/Assets/AssetImage.png")]
public sealed class Sprite : FileAsset
{
    public string Format { get; set; } = "BEngine.Sprite";
    public int Version { get; set; } = 1;
    public string Texture { get; set; } = string.Empty;
    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 0.5f;

    [YamlIgnore]
    public Vector2 pivot => new((Fix64)(double)PivotX, (Fix64)(double)PivotY);

    [YamlIgnore]
    internal string packedAtlas { get; set; } = string.Empty;

    [YamlIgnore]
    internal string packedRegion { get; set; } = string.Empty;

    public void Validate()
    {
        if (!Format.Equals("BEngine.Sprite", StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException($"Unsupported sprite format/version '{Format}' v{Version}.");
        if (string.IsNullOrWhiteSpace(Texture))
            throw new InvalidDataException("A Sprite requires a texture asset reference.");
        if (!float.IsFinite(PivotX) || !float.IsFinite(PivotY) ||
            PivotX < 0 || PivotX > 1 || PivotY < 0 || PivotY > 1)
            throw new InvalidDataException($"Sprite '{name}' has an invalid pivot.");
        Texture = Texture.Replace('\\', '/').Trim();
    }

    public static Sprite Load(string path)
    {
        var fullPath = AssetReferencePath.Resolve(path);
        var sprite = YamlUtility.Load<Sprite>(fullPath);
        sprite.assetPath = AssetReferencePath.ToReference(fullPath);
        sprite.sourcePath = fullPath;
        if (string.IsNullOrWhiteSpace(sprite.name))
            sprite.name = Path.GetFileNameWithoutExtension(
                Path.GetFileNameWithoutExtension(fullPath));
        sprite.Validate();
        return sprite;
    }

    public void Save(string path)
    {
        var fullPath = AssetReferencePath.Resolve(path);
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fullPath));
        Validate();
        YamlUtility.Save(this, fullPath);
        assetPath = AssetReferencePath.ToReference(fullPath);
        sourcePath = fullPath;
    }

    internal static Sprite FromTexture(
        string texture, Vector2 pivot, string reference,
        string atlas = "", string region = "") => new()
    {
        name = Path.GetFileNameWithoutExtension(texture),
        Texture = texture.Replace('\\', '/').Trim(),
        PivotX = (float)pivot.x,
        PivotY = (float)pivot.y,
        assetPath = reference.Replace('\\', '/').Trim(),
        packedAtlas = atlas.Replace('\\', '/').Trim(),
        packedRegion = region.Trim()
    };
}
