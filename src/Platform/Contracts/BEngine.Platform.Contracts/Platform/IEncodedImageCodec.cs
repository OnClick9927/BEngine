namespace BEngine.Platform;

/// <summary>Optional image capability supplied by a loaded module. No decoder is initialized by Core.</summary>
public interface IEncodedImageCodec
{
    bool TryGetInfo(byte[] bytes, out int width, out int height);
    bool TryDecode(ReadOnlySpan<byte> bytes, out int width, out int height, out byte[] rgba);
}

public static class EncodedImages
{
    public static IEncodedImageCodec? Current { get; set; }
    public static bool TryGetInfo(byte[] bytes, out int width, out int height)
    {
        if (Current is { } codec) return codec.TryGetInfo(bytes, out width, out height);
        width = height = 0; return false;
    }
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out int width, out int height, out byte[] rgba)
    {
        if (Current is { } codec) return codec.TryDecode(bytes, out width, out height, out rgba);
        width = height = 0; rgba = []; return false;
    }
}
