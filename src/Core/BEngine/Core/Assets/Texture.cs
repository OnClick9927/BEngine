namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
public class Texture : BAsset
{
    private Color[]? _pixels;
    private int _pixelVersion;

    public int width { get; internal set; }
    public int height { get; internal set; }
    public bool sRGB { get; internal set; } = true;
    public bool alphaIsTransparency { get; internal set; } = true;
    public bool isReadable { get; internal set; }
    public TextureCompressionFormat compressionFormat { get; internal set; } = TextureCompressionFormat.Automatic;
    public TextureFilterMode filterMode { get; internal set; } = TextureFilterMode.Bilinear;
    public TextureWrapMode wrapMode { get; internal set; } = TextureWrapMode.Clamp;
    public bool mipMaps { get; internal set; }
    public int maxSize { get; internal set; } = 2048;
    public int pixelsPerUnit { get; internal set; } = 100;

    internal Texture() { }

    public Texture(int width, int height, bool linear = false)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        this.width = width;
        this.height = height;
        sRGB = !linear;
        isReadable = true;
        _pixels = new Color[checked(width * height)];
        assetPath = $"memory-texture:{GetInstanceID()}";
        name = "Texture";
    }

    public Sprite CreateSprite(Vector2 pivot, long localIdentifier = 21300000) =>
        Sprite.Create(this, pivot, localIdentifier);

    public Color GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        return ReadablePixels()[y * width + x];
    }

    public void SetPixel(int x, int y, Color color)
    {
        ValidateCoordinates(x, y);
        ReadablePixels()[y * width + x] = color;
    }

    public Color[] GetPixels()
    {
        var pixels = ReadablePixels();
        return (Color[])pixels.Clone();
    }

    public void SetPixels(IReadOnlyList<Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count != checked(width * height))
            throw new ArgumentException("Pixel count must match Texture width * height.", nameof(colors));
        var pixels = ReadablePixels();
        for (var index = 0; index < pixels.Length; index++) pixels[index] = colors[index];
    }

    public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false)
    {
        _ = ReadablePixels();
        mipMaps = updateMipmaps && mipMaps;
        checked { _pixelVersion++; }
        if (makeNoLongerReadable) isReadable = false;
    }

    public byte[] EncodeToPNG()
    {
        var pixels = ReadablePixels();
        var rgba = new byte[checked(pixels.Length * 4)];
        for (var index = 0; index < pixels.Length; index++)
        {
            rgba[index * 4] = ToByte(pixels[index].r);
            rgba[index * 4 + 1] = ToByte(pixels[index].g);
            rgba[index * 4 + 2] = ToByte(pixels[index].b);
            rgba[index * 4 + 3] = ToByte(pixels[index].a);
        }
        return Rendering.PngImageCodec.EncodeRgba(width, height, rgba);
    }

    internal int PixelVersion => _pixelVersion;

    internal bool TryGetRuntimeRgba(out byte[] rgba)
    {
        rgba = [];
        if (!assetPath.StartsWith("memory-texture:", StringComparison.Ordinal) || _pixels is null) return false;
        rgba = new byte[checked(_pixels.Length * 4)];
        for (var index = 0; index < _pixels.Length; index++)
        {
            rgba[index * 4] = ToByte(_pixels[index].r);
            rgba[index * 4 + 1] = ToByte(_pixels[index].g);
            rgba[index * 4 + 2] = ToByte(_pixels[index].b);
            rgba[index * 4 + 3] = ToByte(_pixels[index].a);
        }
        return true;
    }

    internal static bool IsSupportedSourcePath(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);

    private Color[] ReadablePixels()
    {
        if (!isReadable) throw new InvalidOperationException("Texture is not readable.");
        if (_pixels is not null) return _pixels;
        var path = string.IsNullOrWhiteSpace(artifactPath) ? sourcePath : artifactPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !Rendering.PngImageCodec.TryDecode(File.ReadAllBytes(path), out var decodedWidth,
                out var decodedHeight, out var rgba) || decodedWidth != width || decodedHeight != height)
            throw new InvalidDataException($"Texture pixel data for '{name}' could not be decoded.");
        _pixels = new Color[checked(width * height)];
        for (var index = 0; index < _pixels.Length; index++)
            _pixels[index] = new Color(FromByte(rgba[index * 4]), FromByte(rgba[index * 4 + 1]),
                FromByte(rgba[index * 4 + 2]), FromByte(rgba[index * 4 + 3]));
        return _pixels;
    }

    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)height) throw new ArgumentOutOfRangeException(nameof(y));
    }

    private static byte ToByte(Fix64 value) =>
        checked((byte)Math.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255), 0, 255));

    private static Fix64 FromByte(byte value) => (Fix64)value / 255;
}
