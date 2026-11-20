using System.Globalization;
using BEngine.Rendering;

namespace BEngine;

internal sealed class SpriteResourceCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(Sprite);
    public override long SyntheticLocalIdentifier => 21300000;
    public override bool CanDecode(in RuntimeResourceMetadata metadata) =>
        (metadata.LocalIdentifier == 0 || metadata.IsSynthetic && metadata.LocalIdentifier == SyntheticLocalIdentifier) &&
        metadata.Importer.Equals("TextureImporter", StringComparison.OrdinalIgnoreCase) &&
        metadata.Settings.TryGetValue("textureType", out var textureType) && textureType.Equals(nameof(Sprite), StringComparison.OrdinalIgnoreCase);

    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata)
    {
        if (!Texture.IsSupportedSourcePath(metadata.Address) || !ImageCodec.TryGetInfo(bytes.Span, out _, out _))
            throw new InvalidDataException($"Resource Sprite '{metadata.Address}' is not a supported image.");
        var settings = metadata.Settings;
        var sprite = Sprite.FromTexture(metadata.SourcePath,
            new Vector2(Mathf.Clamp01(ReadNumber(settings, "spritePivotX", Fix64.Half)),
                Mathf.Clamp01(ReadNumber(settings, "spritePivotY", Fix64.Half))), metadata.Address);
        sprite.BindAssetFile(metadata.Address, string.Empty, metadata.Guid, nameof(Sprite));
        sprite.border = new(ReadNumber(settings, "spriteBorderLeft", 0), ReadNumber(settings, "spriteBorderBottom", 0),
            ReadNumber(settings, "spriteBorderRight", 0), ReadNumber(settings, "spriteBorderTop", 0));
        sprite.pixelsPerUnit = Fix64.Max(1, ReadNumber(settings, "pixelsPerUnit", 100));
        sprite.BindSourceIdentity(metadata.OwnerGuid.ToString("N"), SyntheticLocalIdentifier, metadata.Address);
        sprite.sourcePath = string.Empty;
        sprite.assetType = nameof(Sprite);
        return sprite;
    }

    private static Fix64 ReadNumber(IReadOnlyDictionary<string, string> settings, string key, Fix64 fallback) =>
        settings.TryGetValue(key, out var text) && Fix64.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? Fix64.Max(0, value) : fallback;
}
