namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
public class Texture : BAsset
{
    private Color[]? _pixels;
    private byte[]? _rgba;
    private int _pixelVersion;

    public int width { get; internal set; }
    public int height { get; internal set; }
    public bool sRGB { get; internal set; } = true;
    public bool alphaIsTransparency { get; internal set; } = true;
    public bool isReadable { get; internal set; }
    public TextureCompressionFormat compressionFormat { get; internal set; } = TextureCompressionFormat.Automatic;
    public TextureFilterMode filterMode
    {
        get;
        set => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    } = TextureFilterMode.Bilinear;
    public TextureWrapMode wrapMode
    {
        get;
        set => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    } = TextureWrapMode.Clamp;
    public bool mipMaps { get; internal set; }
    public int maxSize { get; internal set; } = 2048;
    public int pixelsPerUnit { get; internal set; } = 100;

    internal Texture() { }

    protected override object CaptureClonePayload() => new object?[] {
        width, height, sRGB, alphaIsTransparency, isReadable, (int)compressionFormat,
        mipMaps, maxSize, pixelsPerUnit, _pixels?.Clone(), _pixelVersion,
        assetPath.StartsWith("memory-texture:", StringComparison.Ordinal), _rgba?.Clone() };

    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        if (payload is not object?[] data || data.Length is not (12 or 13)) throw new InvalidDataException("Invalid Texture clone payload.");
        width = (int)data[0]!; height = (int)data[1]!; sRGB = (bool)data[2]!;
        alphaIsTransparency = (bool)data[3]!; isReadable = (bool)data[4]!;
        compressionFormat = (TextureCompressionFormat)(int)data[5]!;
        mipMaps = (bool)data[6]!; maxSize = (int)data[7]!; pixelsPerUnit = (int)data[8]!;
        _pixels = (Color[]?)data[9]; _pixelVersion = (int)data[10]!;
        if ((bool)data[11]!) assetPath = $"memory-texture:{GetInstanceID()}";
        _rgba = data.Length == 13 ? (byte[]?)data[12] : null;
    }

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

    /// <summary>Creates a runtime texture from top-down RGBA8 bytes without expanding to Color[].</summary>
    public static Texture FromRgba32(int width, int height, ReadOnlySpan<byte> rgba, bool linear = false)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA byte count must match positive texture dimensions.", nameof(rgba));
        var texture = new Texture { width = width, height = height, sRGB = !linear, isReadable = true, name = "Texture", _rgba = rgba.ToArray() };
        texture.assetPath = $"memory-texture:{texture.GetInstanceID()}"; return texture;
    }

    /// <summary>Copies top-down RGBA8 data. Call Apply to publish the changed pixels to renderers.</summary>
    public void SetPixelData(ReadOnlySpan<byte> rgba)
    {
        EnsureReadable();
        if (rgba.Length != checked(width * height * 4)) throw new ArgumentException("RGBA byte count must match the texture dimensions.", nameof(rgba));
        if (_rgba is null || _rgba.Length != rgba.Length) _rgba = new byte[rgba.Length];
        rgba.CopyTo(_rgba); _pixels = null;
    }

    public Color GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        EnsureReadable();
        if (_rgba is not null)
        {
            var offset = (y * width + x) * 4;
            return new Color(FromByte(_rgba[offset]), FromByte(_rgba[offset + 1]), FromByte(_rgba[offset + 2]), FromByte(_rgba[offset + 3]));
        }
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
        EnsureReadable();
        if (_rgba is null) _ = ReadablePixels();
        mipMaps = updateMipmaps && mipMaps;
        checked { _pixelVersion++; }
        if (makeNoLongerReadable) isReadable = false;
    }

    public byte[] EncodeToPNG()
    {
        EnsureReadable();
        if (_rgba is not null) return Rendering.PngImageCodec.EncodeRgba(width, height, _rgba);
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
        if (!assetPath.StartsWith("memory-texture:", StringComparison.Ordinal)) return false;
        if (_rgba is not null) { rgba = (byte[])_rgba.Clone(); return true; }
        if (_pixels is null) return false;
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
        Rendering.ImageCodec.IsSupportedPath(path);

    protected override void ReleaseAssetResources()
    {
        _pixels = null;
        _rgba = null;
        isReadable = false;
        base.ReleaseAssetResources();
    }

    private Color[] ReadablePixels()
    {
        EnsureReadable();
        if (_pixels is not null) return _pixels;
        if (_rgba is not null)
        {
            _pixels = new Color[checked(width * height)];
            for (var i = 0; i < _pixels.Length; i++)
                _pixels[i] = new Color(FromByte(_rgba[i * 4]), FromByte(_rgba[i * 4 + 1]), FromByte(_rgba[i * 4 + 2]), FromByte(_rgba[i * 4 + 3]));
            _rgba = null; return _pixels;
        }
        var path = string.IsNullOrWhiteSpace(artifactPath) ? sourcePath : artifactPath;
        var reference = parentAssetGuid is { } owner && localIdentifier > 0
            ? $"guid:{owner:N}#subasset={localIdentifier.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : assetPath;
        var bytes = !string.IsNullOrWhiteSpace(path) && RuntimeServiceAccess.FileSystem.FileExists(path)
            ? RuntimeServiceAccess.FileSystem.ReadAllBytes(path)
            : string.IsNullOrWhiteSpace(reference) ? null : ResourceLoader.Load<byte[]>(reference, "Resources");
        if (bytes is null || !Rendering.ImageCodec.TryDecode(bytes, out var decodedWidth,
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

    private void EnsureReadable()
    { ThrowIfDestroyed(); if (!isReadable) throw new InvalidOperationException("Texture is not readable."); }

    private static byte ToByte(Fix64 value) =>
        checked((byte)Math.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255), 0, 255));

    private static Fix64 FromByte(byte value) => (Fix64)value / 255;
}
