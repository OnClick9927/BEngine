using System.Runtime.InteropServices;

namespace BEngine.Platform;

/// <summary>Windowless Media Foundation container I/O. No engine asset, playback or mixer policy.</summary>
public sealed class WindowsMediaDecoder : IPlatformVideoDecoder
{
    public static WindowsMediaDecoder Shared { get; } = new();
    public PlatformVideoDecoderCapabilities Capabilities => new(OperatingSystem.IsWindows(), true, true);
    public int ActiveSessions => WindowsMediaSession.ActiveSessions;
    public IPlatformVideoFrameReader OpenVideo(string source, ReadOnlyMemory<byte> bytes, PlatformVideoOpenOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode)) throw new ArgumentOutOfRangeException(nameof(options));
        var data = BorrowBytes(bytes);
        if (options.Mode == PlatformVideoDecodeMode.Software)
            return new WindowsVideoFrameReader(source, data, false, false, options.PrepareDimensions);
        try { return new WindowsVideoFrameReader(source, data, true, options.Mode == PlatformVideoDecodeMode.HardwareRequired, options.PrepareDimensions); }
        catch (Exception exception) when (options.Mode == PlatformVideoDecodeMode.Automatic && exception is InvalidDataException or NotSupportedException)
        { return new WindowsVideoFrameReader(source, data, false, false, options.PrepareDimensions); }
    }
    public IPlatformAudioSampleReader OpenAudio(string source, ReadOnlyMemory<byte> bytes)
    { ArgumentException.ThrowIfNullOrWhiteSpace(source); return new WindowsVideoAudioReader(source, BorrowBytes(bytes)); }
    // SHCreateMemStream takes its own native copy during Open; avoid a second managed copy of an entire container.
    private static byte[]? BorrowBytes(ReadOnlyMemory<byte> bytes) => bytes.IsEmpty ? null :
        MemoryMarshal.TryGetArray(bytes, out var array) && array.Offset == 0 && array.Count == array.Array!.Length
            ? array.Array : bytes.ToArray();
}
