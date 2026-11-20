namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsTextureDescription(
    int Width,
    int Height,
    GraphicsTextureFormat Format,
    GraphicsTextureUsage Usage,
    GraphicsTextureFilter MinFilter = GraphicsTextureFilter.Linear,
    GraphicsTextureFilter MagFilter = GraphicsTextureFilter.Linear,
    GraphicsTextureAddressMode AddressMode = GraphicsTextureAddressMode.ClampToEdge,
    int MipLevels = 1,
    bool Trilinear = false)
{
    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (Usage == GraphicsTextureUsage.None) throw new ArgumentOutOfRangeException(nameof(Usage));
        if (!Enum.IsDefined(MinFilter)) throw new ArgumentOutOfRangeException(nameof(MinFilter));
        if (!Enum.IsDefined(MagFilter)) throw new ArgumentOutOfRangeException(nameof(MagFilter));
        if (!Enum.IsDefined(AddressMode)) throw new ArgumentOutOfRangeException(nameof(AddressMode));
        if (!Enum.IsDefined(Format)) throw new ArgumentOutOfRangeException(nameof(Format));
        if (MipLevels < 1 || MipLevels > 1 + (int)Math.Log2(Math.Max(Width, Height)))
            throw new ArgumentOutOfRangeException(nameof(MipLevels));
        if (IsCompressed && Usage != GraphicsTextureUsage.Sampled)
            throw new NotSupportedException("Block-compressed textures must be sampled-only.");
    }

    public bool IsCompressed => Format is GraphicsTextureFormat.Bc1RgbaUnorm or
        GraphicsTextureFormat.Bc3RgbaUnorm or GraphicsTextureFormat.Bc7RgbaUnorm or
        GraphicsTextureFormat.Etc2Rgba8Unorm or GraphicsTextureFormat.Astc4x4Unorm;
    public int LevelByteSize(int level)
    {
        if ((uint)level >= MipLevels) throw new ArgumentOutOfRangeException(nameof(level));
        var width = Math.Max(1, Width >> level); var height = Math.Max(1, Height >> level);
        return IsCompressed ? checked(((width + 3) / 4) * ((height + 3) / 4) *
            (Format == GraphicsTextureFormat.Bc1RgbaUnorm ? 8 : 16)) :
            checked(width * height * (Format == GraphicsTextureFormat.R8Unorm ? 1 : 4));
    }
    public int DataByteSize
    {
        get { var result = 0; for (var i = 0; i < MipLevels; i++) result = checked(result + LevelByteSize(i)); return result; }
    }
}
