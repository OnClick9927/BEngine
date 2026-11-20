using System.Buffers.Binary;
using System.Text;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Uses the editor host's ICC CMM, including LUT-based CMYK and ICC v4 profiles.</summary>
internal sealed class PhotoshopColorTransform : IDisposable
{
    private IEditorColorTransform? _transform;
    private bool _disposed;
    private readonly int _mode;
    private PhotoshopColorTransform(int mode) => _mode = mode;

    internal static PhotoshopColorTransform Open(DecodedArtDocument document, int mode)
    {
        var result = new PhotoshopColorTransform(mode);
        var profile = Gather699(document.Metadata)?.GetBytes();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.ArtSourceMetadata? Gather699(global::System.Collections.Generic.IEnumerable<global::BEngine.ArtSourceMetadata> items699)
        {
            ArgumentNullException.ThrowIfNull(items699, "source");
            bool Evaluate699(global::BEngine.ArtSourceMetadata item) => item.Scope == "Photoshop.ImageResource" && item.Type == 1039;
            global::System.ReadOnlySpan<global::BEngine.ArtSourceMetadata> values;
            if (items699.GetType() == typeof(global::BEngine.ArtSourceMetadata[]))
                values = (global::BEngine.ArtSourceMetadata[])items699;
            else if (items699.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ArtSourceMetadata>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ArtSourceMetadata>)items699);
            else
                return global::System.Linq.Enumerable.LastOrDefault(items699, Evaluate699);
            for (var index = values.Length - 1; index >= 0; index--)
            {
                var item = values[index];
                if (Evaluate699(item))
                    return item;
            }

            return default!;
        }
        if (profile is null || profile.Length == 0) return result;
        try
        {
            if (profile.Length < 128 || profile.Length > 16 * 1024 * 1024 || Encoding.ASCII.GetString(profile, 36, 4) != "acsp" ||
                BinaryPrimitives.ReadUInt32BigEndian(profile) != profile.Length)
                throw new InvalidDataException("Photoshop embedded ICC profile is malformed.");
            var expected = mode == 4 ? "CMYK" : mode == 1 ? "GRAY" : "RGB ";
            if (Encoding.ASCII.GetString(profile, 16, 4) != expected) throw new InvalidDataException("Photoshop ICC profile does not match its color mode.");
            result._transform = PlatformRuntime.GetRequiredService<IEditorColorManagementHost>().OpenToSrgb(profile,
                mode == 4 ? EditorColorSpace.Cmyk : mode == 1 ? EditorColorSpace.Gray : EditorColorSpace.Rgb);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal void Convert(byte[] rgba, Dictionary<short, byte[]> channels, List<ArtPrecisionChannel> precision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transform is null && _mode != 4) return;
        var count = _mode == 4 ? 4 : _mode == 1 ? 1 : 3;
        for (short channel = 0; channel < count; channel++)
            if (!channels.ContainsKey(channel)) throw new InvalidDataException("Photoshop pixel layer is missing a color channel.");
        var raw = precision.Where(channel => channel.Id >= 0 && channel.Id < count)
            .ToDictionary(channel => channel.Id, channel => (channel.BitDepth, Bytes: System.Convert.FromBase64String(channel.SamplesBase64)));
        Span<EditorColorSample> input = stackalloc EditorColorSample[256];
        Span<EditorColorSample> output = stackalloc EditorColorSample[256];
        for (var start = 0; start < rgba.Length / 4; start += input.Length)
        {
            var length = Math.Min(input.Length, rgba.Length / 4 - start);
            for (var offset = 0; offset < length; offset++)
            {
                var pixel = start + offset;
                var first = Sample(0, pixel); var second = count > 1 ? Sample(1, pixel) : first; var third = count > 1 ? Sample(2, pixel) : first;
                var fourth = count > 3 ? Sample(3, pixel) : (ushort)0;
                input[offset] = new(first, second, third, fourth);
                if (_transform is null)
                {
                    // Untagged Photoshop CMYK stores inverted ink channels. No device profile is assumed.
                    rgba[pixel * 4] = (byte)((long)(65535 - first) * (65535 - fourth) * 255 / (65535L * 65535));
                    rgba[pixel * 4 + 1] = (byte)((long)(65535 - second) * (65535 - fourth) * 255 / (65535L * 65535));
                    rgba[pixel * 4 + 2] = (byte)((long)(65535 - third) * (65535 - fourth) * 255 / (65535L * 65535));
                }
            }
            if (_transform is null) continue;
            _transform.Convert(input[..length], output[..length]);
            for (var offset = 0; offset < length; offset++)
            {
                var target = (start + offset) * 4; var value = output[offset];
                rgba[target] = (byte)((value.X + 128) / 257); rgba[target + 1] = (byte)((value.Y + 128) / 257); rgba[target + 2] = (byte)((value.Z + 128) / 257);
            }
        }
        ushort Sample(short channel, int pixel)
        {
            var value = (ushort)(channels[channel][pixel] * 257);
            if (raw.TryGetValue(channel, out var data))
                value = data.BitDepth == 16 ? BinaryPrimitives.ReadUInt16BigEndian(data.Bytes.AsSpan(pixel * 2))
                    : (ushort)Math.Round(Math.Clamp(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data.Bytes.AsSpan(pixel * 4))), 0, 1) * 65535);
            return _mode == 4 ? (ushort)(65535 - value) : value;
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        var transform = _transform; _transform = null; transform?.Dispose();
    }
}
