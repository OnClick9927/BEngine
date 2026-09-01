using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BEngine.Editor;
using EditorAssetPreview = BEngine.Editor.AssetPreview;
using EditorTextAsset = BEngine.Editor.TextAsset;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace BEngine.ExampleTests.AssetPreview;

internal sealed class AssetPreviewFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BEngineAssetPreview",
        Guid.NewGuid().ToString("N"));

    internal AssetPreviewFixture() => Directory.CreateDirectory(_root);

    internal DefaultAsset CreateTexture(string name, int width, int height, Color color)
    {
        var path = Path.Combine(_root, name + ".png");
        File.WriteAllBytes(path, EncodePng(width, height, color));
        return CreateDefaultAsset(name, path, "Texture");
    }

    internal DefaultAsset CreateBitmap(string name, int width, int height, Color color) =>
        CreateWindowsRaster(name, ".bmp", width, height, color, DrawingImageFormat.Bmp);

    internal DefaultAsset CreateJpeg(string name, int width, int height, Color color) =>
        CreateWindowsRaster(name, ".jpg", width, height, color, DrawingImageFormat.Jpeg);

    internal EditorTextAsset CreateText(string name, string text)
    {
        var path = Path.Combine(_root, name + ".txt");
        File.WriteAllText(path, text);
        var asset = new EditorTextAsset { name = name };
        Set(asset, nameof(DefaultAsset.assetPath), path);
        Set(asset, nameof(DefaultAsset.sourcePath), path);
        Set(asset, nameof(DefaultAsset.assetType), "Text");
        Set(asset, nameof(EditorTextAsset.text), text);
        return asset;
    }

    internal (DefaultAsset Asset, string TexturePath) CreateAtlas()
    {
        var first = Path.Combine(_root, "atlas-red.png");
        var second = Path.Combine(_root, "atlas-blue.png");
        File.WriteAllBytes(first, EncodePng(12, 6, new Color(1, 0, 0, 1)));
        File.WriteAllBytes(second, EncodePng(6, 12, new Color(0, 0, 1, 1)));
        var manifest = Path.Combine(_root, "Generated.atlas.yaml");
        var atlas = new TextureAtlas
        {
            MaxSize = 64,
            Padding = 1,
            Extrude = 1,
            Sources = [CreateSprite(first, "red"), CreateSprite(second, "blue")]
        };
        atlas.Save(manifest);
        TextureAtlasBuilder.Build(atlas, manifest);
        var texturePath = Path.Combine(_root, "Generated.png");
        TestAssert.Require(File.Exists(texturePath), "TextureAtlasBuilder did not generate the preview PNG.");
        return (CreateDefaultAsset("Generated", manifest, nameof(TextureAtlas)), texturePath);
    }

    internal DefaultAsset CreateMissingTexture()
    {
        var path = Path.Combine(_root, "missing-preview.png");
        return CreateDefaultAsset("Missing Preview", path, "Texture");
    }

    internal void RewritePng(string path, int width, int height, Color color)
    {
        path = Path.GetFullPath(path);
        var before = new FileInfo(path);
        TestAssert.Require(before.Exists, $"Cannot rewrite missing preview PNG '{path}'.");
        var oldLength = before.Length;
        var oldWriteTime = before.LastWriteTimeUtc;
        var bytes = EncodePng(width, height, color);
        if (bytes.LongLength == oldLength) Array.Resize(ref bytes, bytes.Length + 1);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, oldWriteTime.AddSeconds(2));
        var after = new FileInfo(path);
        TestAssert.Require(after.Length != oldLength && after.LastWriteTimeUtc != oldWriteTime,
            "The cache fixture did not change both the PNG length and modification time.");
    }

    internal void RewriteText(string path, string text)
    {
        path = Path.GetFullPath(path);
        var oldWriteTime = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, oldWriteTime.AddSeconds(2));
    }

    public void Dispose()
    {
        EditorAssetPreview.ClearTemporaryAssetPreviews();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DefaultAsset CreateDefaultAsset(string name, string path, string type)
    {
        var asset = new DefaultAsset { name = name };
        Set(asset, nameof(DefaultAsset.assetPath), path);
        Set(asset, nameof(DefaultAsset.sourcePath), path);
        Set(asset, nameof(DefaultAsset.assetType), type);
        return asset;
    }

    private static Sprite CreateSprite(string texturePath, string name)
    {
        BAsset.Invalidate(texturePath);
        var texture = BAsset.Load<Texture>(texturePath) ??
                      throw new InvalidOperationException($"Texture '{texturePath}' could not be loaded.");
        var sprite = texture.CreateSprite(new Vector2(Fix64.Half, Fix64.Half));
        sprite.name = name;
        return sprite;
    }

    private DefaultAsset CreateWindowsRaster(string name, string extension, int width, int height,
        Color color, DrawingImageFormat format)
    {
        var path = Path.Combine(_root, name + extension);
        using var bitmap = new DrawingBitmap(width, height);
        using (var graphics = DrawingGraphics.FromImage(bitmap))
            graphics.Clear(DrawingColor.FromArgb(Channel(color.a), Channel(color.r), Channel(color.g),
                Channel(color.b)));
        bitmap.Save(path, format);
        return CreateDefaultAsset(name, path, "Texture");
    }

    private static void Set(object target, string propertyName, object value) =>
        target.GetType().GetProperty(propertyName, Members)!.SetValue(target, value);

    private static byte[] EncodePng(int width, int height, Color color)
    {
        var scanlines = new byte[checked(height * (width * 4 + 1))];
        var red = Channel(color.r);
        var green = Channel(color.g);
        var blue = Channel(color.b);
        var alpha = Channel(color.a);
        for (var y = 0; y < height; y++)
        {
            var offset = y * (width * 4 + 1) + 1;
            for (var x = 0; x < width; x++)
            {
                scanlines[offset++] = red;
                scanlines[offset++] = green;
                scanlines[offset++] = blue;
                scanlines[offset++] = alpha;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(scanlines);

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string name, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var type = Encoding.ASCII.GetBytes(name);
        stream.Write(type);
        stream.Write(data);
        var crcData = new byte[type.Length + data.Length];
        type.CopyTo(crcData, 0);
        data.CopyTo(crcData.AsSpan(type.Length));
        BinaryPrimitives.WriteUInt32BigEndian(length, Crc32(crcData));
        stream.Write(length);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xedb88320u ^ crc >> 1 : crc >> 1;
        }
        return ~crc;
    }

    private static byte Channel(Fix64 value) =>
        (byte)Math.Clamp((int)Math.Round((double)value * 255), 0, 255);
}
