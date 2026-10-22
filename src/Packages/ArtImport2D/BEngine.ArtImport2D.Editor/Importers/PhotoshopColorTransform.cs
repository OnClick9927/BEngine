using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace BEngine.Editor;

/// <summary>Uses the editor host's ICC CMM, including LUT-based CMYK and ICC v4 profiles.</summary>
internal sealed class PhotoshopColorTransform : IDisposable
{
    private IntPtr _source, _destination, _transform;
    private readonly int _mode;
    private readonly NativeColor[] _input = new NativeColor[4096], _output = new NativeColor[4096];
    private PhotoshopColorTransform(int mode) => _mode = mode;

    internal static PhotoshopColorTransform Open(DecodedArtDocument document, int mode)
    {
        var result = new PhotoshopColorTransform(mode);
        var profile = document.Metadata.LastOrDefault(item => item.Scope == "Photoshop.ImageResource" && item.Type == 1039)?.GetBytes();
        if (profile is null || profile.Length == 0) return result;
        try
        {
            if (profile.Length < 128 || profile.Length > 16 * 1024 * 1024 || Encoding.ASCII.GetString(profile, 36, 4) != "acsp" ||
                BinaryPrimitives.ReadUInt32BigEndian(profile) != profile.Length)
                throw new InvalidDataException("Photoshop embedded ICC profile is malformed.");
            var expected = mode == 4 ? "CMYK" : mode == 1 ? "GRAY" : "RGB ";
            if (Encoding.ASCII.GetString(profile, 16, 4) != expected) throw new InvalidDataException("Photoshop ICC profile does not match its color mode.");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ICC art import requires the Windows editor color-management host.");
            result._source = OpenMemory(profile);
            uint length = 0; GetStandardColorSpaceProfileW(null, 0x73524742, null, ref length);
            if (length is < 2 or > 65536) throw new InvalidDataException("The editor host has no standard sRGB profile.");
            var name = new StringBuilder((int)length / 2);
            if (!GetStandardColorSpaceProfileW(null, 0x73524742, name, ref length)) throw Error("Resolve sRGB profile");
            result._destination = OpenMemory(File.ReadAllBytes(name.ToString()));
            result._transform = CreateMultiProfileTransform([result._source, result._destination], 2, [1u], 1, 0, 0);
            if (result._transform == IntPtr.Zero) throw Error("Create ICC transform");
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal void Convert(byte[] rgba, Dictionary<short, byte[]> channels, List<ArtPrecisionChannel> precision)
    {
        if (_transform == IntPtr.Zero && _mode != 4) return;
        var count = _mode == 4 ? 4 : _mode == 1 ? 1 : 3;
        for (short channel = 0; channel < count; channel++)
            if (!channels.ContainsKey(channel)) throw new InvalidDataException("Photoshop pixel layer is missing a color channel.");
        var raw = precision.Where(channel => channel.Id >= 0 && channel.Id < count)
            .ToDictionary(channel => channel.Id, channel => (channel.BitDepth, Bytes: System.Convert.FromBase64String(channel.SamplesBase64)));
        for (var start = 0; start < rgba.Length / 4; start += _input.Length)
        {
            var length = Math.Min(_input.Length, rgba.Length / 4 - start);
            for (var offset = 0; offset < length; offset++)
            {
                var pixel = start + offset;
                var first = Sample(0, pixel); var second = count > 1 ? Sample(1, pixel) : first; var third = count > 1 ? Sample(2, pixel) : first;
                var fourth = count > 3 ? Sample(3, pixel) : (ushort)0;
                _input[offset] = new(first, second, third, fourth);
                if (_transform == IntPtr.Zero)
                {
                    // Untagged Photoshop CMYK stores inverted ink channels. No device profile is assumed.
                    rgba[pixel * 4] = (byte)((long)(65535 - first) * (65535 - fourth) * 255 / (65535L * 65535));
                    rgba[pixel * 4 + 1] = (byte)((long)(65535 - second) * (65535 - fourth) * 255 / (65535L * 65535));
                    rgba[pixel * 4 + 2] = (byte)((long)(65535 - third) * (65535 - fourth) * 255 / (65535L * 65535));
                }
            }
            if (_transform == IntPtr.Zero) continue;
            if (_mode == 4)
            {
                var inks = new byte[length * 4]; var rgb = new byte[length * 3];
                for (var offset = 0; offset < length; offset++)
                {
                    // BM_CMYKQUADS is a CMYK DWORD: little-endian memory stores K,Y,M,C.
                    var value = _input[offset]; inks[offset * 4] = (byte)(value.D / 257); inks[offset * 4 + 1] = (byte)(value.C / 257);
                    inks[offset * 4 + 2] = (byte)(value.B / 257); inks[offset * 4 + 3] = (byte)(value.A / 257);
                }
                if (!TranslateBitmapBits(_transform, inks, 0x0020, (uint)length, 1, (uint)inks.Length, rgb, 0x0004, (uint)rgb.Length, IntPtr.Zero, IntPtr.Zero)) throw Error("Convert ICC CMYK pixels");
                for (var offset = 0; offset < length; offset++) _output[offset] = new((ushort)(rgb[offset * 3] * 257), (ushort)(rgb[offset * 3 + 1] * 257), (ushort)(rgb[offset * 3 + 2] * 257), 0);
            }
            else if (!TranslateColors(_transform, _input, (uint)length, _mode == 1 ? 1u : 2u, _output, 2)) throw Error("Convert ICC pixels");
            for (var offset = 0; offset < length; offset++)
            {
                var target = (start + offset) * 4; var value = _output[offset];
                rgba[target] = (byte)((value.A + 128) / 257); rgba[target + 1] = (byte)((value.B + 128) / 257); rgba[target + 2] = (byte)((value.C + 128) / 257);
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
    private static IntPtr OpenMemory(byte[] bytes)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { var profile = new NativeProfile { Type = 2, Data = pin.AddrOfPinnedObject(), Size = (uint)bytes.Length };
            var handle = OpenColorProfileW(ref profile, 1, 1, 3); return handle != IntPtr.Zero ? handle : throw Error("Open ICC profile"); }
        finally { pin.Free(); }
    }
    private static InvalidDataException Error(string operation) => new($"{operation} failed (Windows color management error {Marshal.GetLastWin32Error()}).");
    public void Dispose()
    {
        if (_transform != IntPtr.Zero) { DeleteColorTransform(_transform); _transform = IntPtr.Zero; }
        if (_destination != IntPtr.Zero) { CloseColorProfile(_destination); _destination = IntPtr.Zero; }
        if (_source != IntPtr.Zero) { CloseColorProfile(_source); _source = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeProfile { internal uint Type; internal IntPtr Data; internal uint Size; }
    [StructLayout(LayoutKind.Sequential, Size = 8)] private struct NativeColor(ushort a, ushort b, ushort c, ushort d) { internal ushort A = a, B = b, C = c, D = d; }
    [DllImport("mscms.dll", SetLastError = true)] private static extern IntPtr OpenColorProfileW(ref NativeProfile profile, uint access, uint share, uint creation);
    [DllImport("mscms.dll")] private static extern bool CloseColorProfile(IntPtr profile);
    [DllImport("mscms.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool GetStandardColorSpaceProfileW(string? machine, uint space, StringBuilder? name, ref uint size);
    [DllImport("mscms.dll", SetLastError = true)] private static extern IntPtr CreateMultiProfileTransform(IntPtr[] profiles, uint count, uint[] intents, uint intentCount, uint flags, uint preferred);
    [DllImport("mscms.dll", SetLastError = true)] private static extern bool TranslateColors(IntPtr transform, [In] NativeColor[] input, uint count, uint inputType, [Out] NativeColor[] output, uint outputType);
    [DllImport("mscms.dll", SetLastError = true)] private static extern bool TranslateBitmapBits(IntPtr transform, byte[] input, uint inputFormat, uint width, uint height, uint inputStride, [Out] byte[] output, uint outputFormat, uint outputStride, IntPtr callback, IntPtr user);
    [DllImport("mscms.dll")] private static extern bool DeleteColorTransform(IntPtr transform);
}
