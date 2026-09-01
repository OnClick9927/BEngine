using YamlDotNet.Serialization;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
public sealed class Sprite : BObject
{
    public string Texture { get; set; } = string.Empty;
    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 0.5f;
    public string OwnerGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; } = 21300000;
    [YamlIgnore, HideInInspector] public string assetPath { get; internal set; } = string.Empty;
    [YamlIgnore, HideInInspector] public string sourcePath { get; internal set; } = string.Empty;
    [YamlIgnore, HideInInspector] public string assetType { get; internal set; } = nameof(Sprite);
    [YamlIgnore] public Vector2 pivot => new((Fix64)(double)PivotX, (Fix64)(double)PivotY);
    [YamlIgnore] internal string packedAtlas { get; set; } = string.Empty;
    [YamlIgnore] internal string packedRegion { get; set; } = string.Empty;
    // Public for YAML reference reconstruction. Runtime code should use Texture.CreateSprite/Sprite.Create.
    public Sprite() { }
    public static Sprite Create(Texture texture, Vector2 pivot, long localIdentifier = 21300000)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return Create(texture.assetPath, pivot, texture.assetPath, texture.Id.ToString("N"), localIdentifier);
    }
    internal static Sprite Create(string texture, Vector2 pivot, string reference, string ownerGuid = "",
        long localIdentifier = 21300000, string atlas = "", string region = "")
    {
        texture = texture?.Replace('\\', '/').Trim() ?? string.Empty;
        var sprite = new Sprite { name = Path.GetFileNameWithoutExtension(texture), Texture = texture,
            PivotX = (float)Mathf.Clamp01(pivot.x), PivotY = (float)Mathf.Clamp01(pivot.y),
            OwnerGuid = ownerGuid?.Trim() ?? string.Empty, LocalIdentifier = localIdentifier,
            assetPath = reference?.Replace('\\', '/').Trim() ?? string.Empty,
            packedAtlas = atlas?.Replace('\\', '/').Trim() ?? string.Empty, packedRegion = region?.Trim() ?? string.Empty };
        if (Guid.TryParse(sprite.OwnerGuid, out var id)) sprite.Id = id;
        return sprite;
    }
    internal void BindSourceIdentity(string ownerGuid, long localIdentifier, string assetReference)
    {
        OwnerGuid = ownerGuid?.Trim() ?? string.Empty; LocalIdentifier = localIdentifier;
        assetPath = assetReference?.Replace('\\', '/').Trim() ?? string.Empty;
        if (Guid.TryParse(OwnerGuid, out var id)) Id = id;
    }
    internal void BindAssetFile(string projectPath, string physicalPath, Guid id, string type)
    {
        assetPath = projectPath; sourcePath = physicalPath; Id = id; assetType = type;
    }
    internal void BindSubAssetReference(string projectPath, Guid parentGuid, long localIdentifier, Guid sourceId) =>
        BindSourceIdentity(parentGuid.ToString("N"), localIdentifier, projectPath);
    internal static Sprite FromTexture(string texture, Vector2 pivot, string reference,
        string atlas = "", string region = "") => Create(texture, pivot, reference, atlas: atlas, region: region);
}
