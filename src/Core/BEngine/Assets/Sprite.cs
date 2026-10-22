using YamlDotNet.Serialization;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
public sealed class Sprite : BObject
{
    [NonSerialized] private BEngine.Texture? _texture;
    public string Texture
    {
        get;
        set
        {
            if (_texture is not null && _texture.assetPath != value) _texture = null;
            field = value;
        }
    } = string.Empty;
    [YamlIgnore, HideInInspector] public BEngine.Texture? texture =>
        _texture ??= BAsset.Load<BEngine.Texture>(Texture);
    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 0.5f;
    /// <summary>Left, bottom, right, top borders in source pixels.</summary>
    public Vector4 border { get; set; }
    public Fix64 pixelsPerUnit { get; set; } = 100;
    public string OwnerGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; } = 21300000;
    [YamlIgnore, HideInInspector] public string assetPath { get; internal set; } = string.Empty;
    [YamlIgnore, HideInInspector] public string sourcePath { get; internal set; } = string.Empty;
    [YamlIgnore, HideInInspector] public string assetType { get; internal set; } = nameof(Sprite);
    [YamlIgnore] public Vector2 pivot => new((Fix64)(double)PivotX, (Fix64)(double)PivotY);
    [YamlIgnore] internal string packedSource { get; set; } = string.Empty;
    [YamlIgnore] internal string packedEntry { get; set; } = string.Empty;
    // Public for YAML reference reconstruction. Runtime code should use Texture.CreateSprite/Sprite.Create.
    public Sprite() { }

    internal void CopyClonePayloadFrom(Sprite source, Func<BObject, BObject> cloneObject)
    {
        _texture = source.texture is { } texture ? (BEngine.Texture)cloneObject(texture) : null;
        if (_texture is not null) Texture = _texture.assetPath;
        packedSource = source.packedSource;
        packedEntry = source.packedEntry;
        sourcePath = source.sourcePath;
        assetPath = source.assetPath;
        assetType = source.assetType;
    }
    public static Sprite Create(Texture texture, Vector2 pivot, long localIdentifier = 21300000)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var sprite = Create(texture.assetPath, pivot, texture.assetPath, texture.Id.ToString("N"), localIdentifier);
        sprite.pixelsPerUnit = texture.pixelsPerUnit;
        sprite._texture = texture;
        return sprite;
    }
    internal static Sprite Create(string texture, Vector2 pivot, string reference, string ownerGuid = "",
        long localIdentifier = 21300000, string source = "", string entry = "")
    {
        texture = texture?.Replace('\\', '/').Trim() ?? string.Empty;
        var sprite = new Sprite
        {
            name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(texture), Texture = texture,
            PivotX = (float)Mathf.Clamp01(pivot.x), PivotY = (float)Mathf.Clamp01(pivot.y),
            OwnerGuid = ownerGuid?.Trim() ?? string.Empty, LocalIdentifier = localIdentifier,
            assetPath = reference?.Replace('\\', '/').Trim() ?? string.Empty,
            packedSource = source?.Replace('\\', '/').Trim() ?? string.Empty,
            packedEntry = entry?.Trim() ?? string.Empty
        };
        if (Guid.TryParse(sprite.OwnerGuid, out var id)) sprite.Id = id;
        const string memoryPrefix = "memory-texture:";
        if (texture.StartsWith(memoryPrefix, StringComparison.Ordinal) &&
            int.TryParse(texture.AsSpan(memoryPrefix.Length), out var instanceId))
            sprite._texture = BObject.FindObjectFromInstanceID(instanceId) as BEngine.Texture;
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
        string source = "", string entry = "") => Create(texture, pivot, reference, source: source, entry: entry);
}
