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
    public Fix64 PivotX { get; set; } = Fix64.Half;
    public Fix64 PivotY { get; set; } = Fix64.Half;
    /// <summary>Left, bottom, right, top borders in source pixels.</summary>
    public Vector4 border { get; set; }
    public Fix64 pixelsPerUnit { get; set; } = 100;
    public string OwnerGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; } = 21300000;
    [YamlIgnore, HideInInspector] public string assetPath { get; internal set; } = string.Empty;
    [YamlIgnore, HideInInspector] public string sourcePath { get; internal set; } = string.Empty;
    [YamlIgnore, HideInInspector] public string assetType { get; internal set; } = nameof(Sprite);
    [YamlIgnore] public Vector2 pivot => new(PivotX, PivotY);
    [YamlIgnore] internal string packedSource { get; set; } = string.Empty;
    [YamlIgnore] internal string packedEntry { get; set; } = string.Empty;
    // Public for YAML reference reconstruction. Runtime code should use Texture.CreateSprite/Sprite.Create.
    public Sprite() { }

    internal void CopyClonePayloadFrom(Sprite source, Func<BObject, BObject> cloneObject)
    {
        RestoreClonePayload(source.CaptureClonePayload(), cloneObject);
    }

    protected override object CaptureClonePayload() => new object?[]
    {
        texture, packedSource, packedEntry, sourcePath, assetPath, assetType
    };

    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        if (payload is not object?[] { Length: 6 } values)
            throw new InvalidDataException("Invalid Sprite clone payload.");
        _texture = values[0] is BObject textureObject ? (BEngine.Texture)cloneObject(textureObject) : null;
        if (_texture is not null) Texture = _texture.assetPath;
        packedSource = (string)values[1]!;
        packedEntry = (string)values[2]!;
        sourcePath = (string)values[3]!;
        assetPath = (string)values[4]!;
        assetType = (string)values[5]!;
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
            name = RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>().GetFileNameWithoutExtension(texture), Texture = texture,
            PivotX = Mathf.Clamp01(pivot.x), PivotY = Mathf.Clamp01(pivot.y),
            OwnerGuid = ownerGuid?.Trim() ?? string.Empty, LocalIdentifier = localIdentifier,
            assetPath = reference?.Replace('\\', '/').Trim() ?? string.Empty,
            packedSource = source?.Replace('\\', '/').Trim() ?? string.Empty,
            packedEntry = entry?.Trim() ?? string.Empty
        };
        if (Guid.TryParse(sprite.OwnerGuid, out var id)) sprite.RestorePersistentIdentity(id);
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
        if (Guid.TryParse(OwnerGuid, out var id)) RestorePersistentIdentity(id);
    }
    internal void BindAssetFile(string projectPath, string physicalPath, Guid id, string type)
    {
        assetPath = projectPath; sourcePath = physicalPath; RestorePersistentIdentity(id); assetType = type;
    }
    internal void BindSubAssetReference(string projectPath, Guid parentGuid, long localIdentifier, Guid sourceId) =>
        BindSourceIdentity(parentGuid.ToString("N"), localIdentifier, projectPath);
    internal static Sprite FromTexture(string texture, Vector2 pivot, string reference,
        string source = "", string entry = "") => Create(texture, pivot, reference, source: source, entry: entry);
}
