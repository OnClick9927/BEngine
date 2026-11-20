using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace BEngine.Platform;

/// <summary>Windows ICC CMM for windowless import tools and interactive editors.</summary>
public sealed class WindowsColorManagementHost : IEditorColorManagementHost
{
    public static WindowsColorManagementHost Shared { get; } = new();
    private static int _active;
    public static int ActiveTransforms => Volatile.Read(ref _active);
    public IEditorColorTransform OpenToSrgb(ReadOnlyMemory<byte> sourceProfile, EditorColorSpace sourceSpace)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This editor host has no Windows color-management service.");
        if (!Enum.IsDefined(sourceSpace)) throw new ArgumentOutOfRangeException(nameof(sourceSpace));
        var span = sourceProfile.Span;
        if (span.Length is < 128 or > 16 * 1024 * 1024 || BinaryPrimitives.ReadUInt32BigEndian(span) != span.Length ||
            !span.Slice(36, 4).SequenceEqual("acsp"u8)) throw new InvalidDataException("The ICC source profile is malformed.");
        var expected = sourceSpace switch { EditorColorSpace.Gray => "GRAY", EditorColorSpace.Cmyk => "CMYK", _ => "RGB " };
        if (Encoding.ASCII.GetString(span.Slice(16, 4)) != expected) throw new InvalidDataException("ICC profile and source component space do not match.");
        return new Transform(sourceProfile.ToArray(), sourceSpace);
    }
    private sealed class Transform : IEditorColorTransform
    {
        private const int BlockSize = 256;
        private IntPtr _source, _destination, _transform;
        private readonly EditorColorSpace _space;
        private readonly NativeColor[] _input, _output;
        private readonly byte[] _inks, _rgb;
        private bool _disposed, _registered;
        internal Transform(byte[] profile, EditorColorSpace space)
        {
            _space = space;
            _input = space == EditorColorSpace.Cmyk ? [] : new NativeColor[BlockSize];
            _output = space == EditorColorSpace.Cmyk ? [] : new NativeColor[BlockSize];
            _inks = space == EditorColorSpace.Cmyk ? new byte[BlockSize * 4] : [];
            _rgb = space == EditorColorSpace.Cmyk ? new byte[BlockSize * 3] : [];
            try
            {
                _source = OpenMemory(profile);
                uint length = 0; GetStandardColorSpaceProfileW(null, 0x73524742, null, ref length);
                if (length is < 2 or > 65536 || (length & 1) != 0) throw new InvalidDataException("The editor host has no standard sRGB profile.");
                var name = new StringBuilder((int)length / 2);
                if (!GetStandardColorSpaceProfileW(null, 0x73524742, name, ref length)) throw Error("Resolve sRGB profile");
                _destination = OpenMemory(File.ReadAllBytes(name.ToString()));
                _transform = CreateMultiProfileTransform([_source, _destination], 2, [1u], 1, 0, 0);
                if (_transform == IntPtr.Zero) throw Error("Create ICC transform");
                Interlocked.Increment(ref _active); _registered = true;
            }
            catch { Dispose(); throw; }
        }
        public void Convert(ReadOnlySpan<EditorColorSample> source, Span<EditorColorSample> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (destination.Length < source.Length) throw new ArgumentException("The output must hold every input color.", nameof(destination));
            if (source.Overlaps(destination, out var overlap) && overlap != 0) throw new ArgumentException("Partially overlapping color buffers are not supported.", nameof(destination));
            for (var start = 0; start < source.Length; start += BlockSize)
            {
                var length = Math.Min(BlockSize, source.Length - start);
                if (_space == EditorColorSpace.Cmyk)
                {
                    for (var i = 0; i < length; i++)
                    {
                        var value = source[start + i];
                        // BM_CMYKQUADS stores a CMYK DWORD as K,Y,M,C in little-endian memory.
                        _inks[i * 4] = (byte)(value.W / 257); _inks[i * 4 + 1] = (byte)(value.Z / 257);
                        _inks[i * 4 + 2] = (byte)(value.Y / 257); _inks[i * 4 + 3] = (byte)(value.X / 257);
                    }
                    if (!TranslateBitmapBits(_transform, _inks, 0x0020, (uint)length, 1, (uint)(length * 4), _rgb, 0x0004, (uint)(length * 3), IntPtr.Zero, IntPtr.Zero)) throw Error("Convert ICC CMYK pixels");
                    for (var i = 0; i < length; i++) destination[start + i] = new((ushort)(_rgb[i * 3] * 257), (ushort)(_rgb[i * 3 + 1] * 257), (ushort)(_rgb[i * 3 + 2] * 257));
                }
                else
                {
                    for (var i = 0; i < length; i++)
                    { var value = source[start + i]; _input[i] = new() { A = value.X, B = value.Y, C = value.Z, D = value.W }; }
                    if (!TranslateColors(_transform, _input, (uint)length, _space == EditorColorSpace.Gray ? 1u : 2u, _output, 2)) throw Error("Convert ICC pixels");
                    for (var i = 0; i < length; i++)
                    { var value = _output[i]; destination[start + i] = new(value.A, value.B, value.C); }
                }
            }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (_transform != IntPtr.Zero) { DeleteColorTransform(_transform); _transform = IntPtr.Zero; }
            if (_destination != IntPtr.Zero) { CloseColorProfile(_destination); _destination = IntPtr.Zero; }
            if (_source != IntPtr.Zero) { CloseColorProfile(_source); _source = IntPtr.Zero; }
            if (_registered) { Interlocked.Decrement(ref _active); _registered = false; }
        }
    }
    private static IntPtr OpenMemory(byte[] bytes)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var profile = new NativeProfile { Type = 2, Data = pin.AddrOfPinnedObject(), Size = (uint)bytes.Length };
            var handle = OpenColorProfileW(ref profile, 1, 1, 3); return handle != IntPtr.Zero ? handle : throw Error("Open ICC profile");
        }
        finally { pin.Free(); }
    }
    private static InvalidDataException Error(string operation) => new($"{operation} failed (Windows color management error {Marshal.GetLastWin32Error()}).");
    [StructLayout(LayoutKind.Sequential)] private struct NativeProfile { internal uint Type; internal IntPtr Data; internal uint Size; }
    // COLOR is a union containing { DWORD; void*; }, hence 16 bytes on x64 and 8 on x86.
    [StructLayout(LayoutKind.Sequential)] private struct Alignment { internal uint Reserved1; internal IntPtr Reserved2; }
    [StructLayout(LayoutKind.Explicit)] private struct NativeColor
    {
        [FieldOffset(0)] internal ushort A; [FieldOffset(2)] internal ushort B;
        [FieldOffset(4)] internal ushort C; [FieldOffset(6)] internal ushort D;
        [FieldOffset(0)] private Alignment Reserved;
    }
    [DllImport("mscms.dll", SetLastError = true)] private static extern IntPtr OpenColorProfileW(ref NativeProfile profile, uint access, uint share, uint creation);
    [DllImport("mscms.dll")] private static extern bool CloseColorProfile(IntPtr profile);
    [DllImport("mscms.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool GetStandardColorSpaceProfileW(string? machine, uint space, StringBuilder? name, ref uint size);
    [DllImport("mscms.dll", SetLastError = true)] private static extern IntPtr CreateMultiProfileTransform(IntPtr[] profiles, uint count, uint[] intents, uint intentCount, uint flags, uint preferred);
    [DllImport("mscms.dll", SetLastError = true)] private static extern bool TranslateColors(IntPtr transform, [In] NativeColor[] input, uint count, uint inputType, [Out] NativeColor[] output, uint outputType);
    [DllImport("mscms.dll", SetLastError = true)] private static extern bool TranslateBitmapBits(IntPtr transform, byte[] input, uint inputFormat, uint width, uint height, uint inputStride, [Out] byte[] output, uint outputFormat, uint outputStride, IntPtr callback, IntPtr user);
    [DllImport("mscms.dll")] private static extern bool DeleteColorTransform(IntPtr transform);
}
