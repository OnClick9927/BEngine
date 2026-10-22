namespace BEngine.TiledMap;

internal static class PngTextureDecoder
{
    internal static bool TryDecode(ReadOnlySpan<byte> data, out int width, out int height, out byte[] rgba) =>
        BEngine.Rendering.ImageCodec.TryDecode(data, out width, out height, out rgba);
}
