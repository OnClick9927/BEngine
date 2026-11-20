using BEngine.Rendering;

namespace BEngine;

internal sealed class TextureResourceCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(Texture);
    public override bool CanDecode(in RuntimeResourceMetadata metadata) => !metadata.IsSynthetic &&
        metadata.AssetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase);
    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata)
    {
        // Metadata-only decode preserves direct upload of imported texture blocks.
        if (!ImageCodec.TryGetInfo(bytes.Span, out var width, out var height))
            throw new InvalidDataException($"Resource Texture '{metadata.Address}' is not a supported image.");
        var texture = new Texture { name = Path.GetFileNameWithoutExtension(metadata.OwnerAddress), width = width, height = height };
        GraphicsAssetLoading.ApplyTextureImportSettings(texture, metadata.Settings);
        return texture;
    }
}

internal sealed class MaterialResourceCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(Material);
    public override bool PreferAssetDecoder => true;
    public override bool CanDecode(in RuntimeResourceMetadata metadata) => metadata.LocalIdentifier == 0;
    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata) => Material.Deserialize(RuntimeAssetDecoding.DecodeText(bytes.Span));
}

internal sealed class ShaderResourceCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(Shader);
    public override bool PreferAssetDecoder => true;
    public override bool CanDecode(in RuntimeResourceMetadata metadata) => metadata.LocalIdentifier == 0;
    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata) =>
        new Shader(Path.GetFileNameWithoutExtension(metadata.Address), RuntimeAssetDecoding.DecodeText(bytes.Span));
}

internal sealed class FontResourceCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(Font);
    public override bool PreferAssetDecoder => true;
    public override bool CanDecode(in RuntimeResourceMetadata metadata) => metadata.LocalIdentifier == 0;
    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata) =>
        new Font { name = Path.GetFileName(metadata.Address) };
}
