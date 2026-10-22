namespace BEngine;

public sealed class ArtSourceMetadata
{
    public string Scope { get; set; } = "";
    public int Frame { get; set; }
    public uint Type { get; set; }
    public string DataBase64 { get; set; } = "";
    public byte[] GetBytes() => Convert.FromBase64String(DataBase64);
}

public sealed class AsepriteSlice2D
{
    public string Name { get; set; } = "";
    public List<AsepriteSliceKey2D> Keys { get; set; } = [];
    public AsepriteSliceKey2D? AtFrame(int frame) => Keys.LastOrDefault(key => key.Frame <= frame);
}

public sealed class AsepriteSliceKey2D
{
    public int Frame { get; set; }
    public Rect Bounds { get; set; }
    public Rect? Center { get; set; }
    public Vector2? Pivot { get; set; }
}

/// <summary>Exact Photoshop channel samples in big-endian source order, before masks or RGBA8 previews.</summary>
public sealed class ArtPrecisionChannel
{
    public int Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int BitDepth { get; set; }
    public string SamplesBase64 { get; set; } = "";
    public float GetSample(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x));
        var bytes = Convert.FromBase64String(SamplesBase64); var offset = checked((y * Width + x) * (BitDepth / 8));
        return BitDepth switch
        {
            16 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)) / 65535f,
            32 => BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset))),
            _ => throw new InvalidDataException("Precision channels require 16-bit integers or 32-bit float samples.")
        };
    }
}
