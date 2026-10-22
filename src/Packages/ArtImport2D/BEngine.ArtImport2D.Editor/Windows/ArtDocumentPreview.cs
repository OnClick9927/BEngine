namespace BEngine.Editor;

public static class ArtDocumentPreview
{
    /// <summary>Builds a bounded RGBA preview with nested group visibility/opacity and common PSD blend modes.</summary>
    public static (int Width, int Height, byte[] Rgba) Compose(ArtDocument2D document, int maximumDimension = 512)
    {
        ArgumentNullException.ThrowIfNull(document); document.Validate();
        if (maximumDimension is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        var scale = Math.Min(1d, maximumDimension / (double)Math.Max(document.CanvasWidth, document.CanvasHeight));
        var width = Math.Max(1, (int)Math.Ceiling(document.CanvasWidth * scale)); var height = Math.Max(1, (int)Math.Ceiling(document.CanvasHeight * scale));
        var output = new byte[width * height * 4]; var groups = 0;
        Draw(output, -1, 0);
        return (width, height, output);
        void Draw(byte[] target, int parent, int depth)
        {
            if (depth > 64 || (long)(depth + 1) * output.Length > 128 * 1024 * 1024) throw new InvalidDataException("Art preview exceeds its nested group/memory budget.");
            Span<byte> color = stackalloc byte[4];
            for (var index = 0; index < document.Layers.Count; index++)
            {
                var layer = document.Layers[index]; if (layer.ParentIndex != parent || !layer.Visible || layer.Opacity == 0) continue;
                if (layer.IsGroup)
                {
                    if (++groups > 4096) throw new InvalidDataException("Art preview exceeds its group budget.");
                    if (layer.BlendMode == "pass" && layer.Opacity == 255) { Draw(target, index, depth + 1); continue; }
                    var pixels = new byte[output.Length]; Draw(pixels, index, depth + 1);
                    for (var pixel = 0; pixel < width * height; pixel++) AsepriteDecoder.CompositeRgba(target.AsSpan(pixel * 4, 4), pixels.AsSpan(pixel * 4, 4), layer.Opacity, Blend(layer.BlendMode));
                    continue;
                }
                if (layer.LoadTexture() is not { } texture) continue;
                var sourcePath = string.IsNullOrEmpty(texture.artifactPath) ? texture.sourcePath : texture.artifactPath;
                byte[] source; var sourceWidth = texture.width; var sourceHeight = texture.height;
                if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath) && BEngine.Rendering.ImageCodec.TryDecode(File.ReadAllBytes(sourcePath), out var decodedWidth, out var decodedHeight, out var decoded))
                { source = decoded; sourceWidth = decodedWidth; sourceHeight = decodedHeight; }
                else
                    source = texture.GetPixels().SelectMany(value => new byte[] { Byte(value.r), Byte(value.g), Byte(value.b), Byte(value.a) }).ToArray();
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var sx = (int)(x / scale) - layer.X; var sy = (int)(y / scale) - layer.Y;
                    if (sx < 0 || sy < 0 || sx >= layer.Width || sy >= layer.Height) continue;
                    var tx = (int)((long)sx * sourceWidth / layer.Width); var ty = (int)((long)sy * sourceHeight / layer.Height);
                    source.AsSpan((ty * sourceWidth + tx) * 4, 4).CopyTo(color);
                    AsepriteDecoder.CompositeRgba(target.AsSpan((y * width + x) * 4, 4), color, layer.Opacity, Blend(layer.BlendMode));
                }
            }
        }
    }
    private static byte Byte(Fix64 value) => (byte)Math.Clamp((int)Math.Round((double)value * 255), 0, 255);
    private static ushort Blend(string name) => name switch
    {
        "mul " => 1, "scrn" => 2, "over" => 3, "dark" => 4, "lite" => 5, "div " => 6, "idiv" => 7,
        "hLit" => 8, "sLit" => 9, "diff" => 10, "smud" => 11, "hue " => 12, "sat " => 13, "colr" => 14, "lum " => 15,
        "lddg" => 16, "fsub" => 17, _ => ushort.TryParse(name, out var value) ? value : (ushort)0
    };
}
