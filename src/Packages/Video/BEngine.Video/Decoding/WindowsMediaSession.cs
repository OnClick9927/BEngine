using System.Runtime.InteropServices;
using static BEngine.Video.WindowsMediaNative;

namespace BEngine.Video;

internal sealed class WindowsMediaSession : IDisposable
{
    private nint _reader, _device, _context, _manager, _stream, _byteStream;
    private bool _comInitialized, _mfInitialized;
    private readonly uint _streamIndex;
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal int CodedWidth { get; private set; }
    internal int CodedHeight { get; private set; }
    internal int CropX { get; private set; }
    internal int CropY { get; private set; }
    internal int VisibleWidth { get; private set; }
    internal int VisibleHeight { get; private set; }
    internal uint RotationDegrees { get; private set; }
    private uint _sourceRotation;
    internal double FramesPerSecond { get; private set; }
    internal double DurationSeconds { get; private set; }
    internal bool CanSeek { get; private set; }
    internal bool HasAudio { get; private set; }
    internal bool HardwareRequested => _device != 0;
    internal uint YuvMatrix { get; private set; }
    internal uint NominalRange { get; private set; }
    internal int DefaultStride { get; private set; }
    internal static int ActiveSessions => Volatile.Read(ref _activeSessions);
    private static int _activeSessions;
    private bool _counted;

    internal WindowsMediaSession(string path, byte[]? bytes, bool audio, bool hardware)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Direct container playback currently requires Windows Media Foundation. Use a portable .bvideo clip on this platform.");
        _streamIndex = audio ? AudioStream : VideoStream;
        nint attributes = 0, type = 0, nativeType = 0;
        try
        {
            var hr = CoInitializeEx(0, 0);
            if (hr >= 0) _comInitialized = true;
            else if (hr != unchecked((int)0x80010106)) Check(hr, "Initialize COM"); // Existing STA is supported.
            Check(MFStartup(0x00020070, 0), "Start Media Foundation"); _mfInitialized = true;
            Check(MFCreateAttributes(out attributes, 4), "Create reader attributes");
            if (hardware)
            {
                Check(D3D11CreateDevice(0, 1, 0, 0x820, 0, 0, 7, out _device, out _, out _context), "Create video decode device");
                var iid = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");
                Check(Method<Query>(_context, 0)(_context, iid, out var multithread), "Get multithread video context");
                try { _ = Method<SetMultithread>(multithread, 5)(multithread, 1); }
                finally { Release(ref multithread); }
                Check(MFCreateDXGIDeviceManager(out var token, out _manager), "Create DXGI manager");
                Check(Method<ResetDevice>(_manager, 7)(_manager, _device, token), "Bind video decode device");
                Check(Method<SetUnknown>(attributes, 27)(attributes, D3DManager, _manager), "Set DXGI manager");
                Set(attributes, HardwareTransforms, 1);
            }
            else Set(attributes, DisableDxva, 1);
            if (bytes is null) Check(MFCreateSourceReaderFromURL(path, attributes, out _reader), "Open video source");
            else
            {
                _stream = SHCreateMemStream(bytes, checked((uint)bytes.Length));
                if (_stream == 0) throw new OutOfMemoryException("Could not create the video memory stream.");
                Check(MFCreateMFByteStreamOnStream(_stream, out _byteStream), "Create media byte stream");
                Check(MFCreateSourceReaderFromByteStream(_byteStream, attributes, out _reader), "Open video bytes");
            }
            var duration = GetPresentation(Duration);
            if (duration.Type is not (20 or 21) || duration.Value <= 0) throw new InvalidDataException("The video must report a finite positive duration.");
            DurationSeconds = duration.Value / 10_000_000d;
            if (DurationSeconds > 48 * 3600) throw new InvalidDataException("Video duration exceeds 48 hours.");
            var characteristics = GetPresentation(Characteristics);
            CanSeek = (characteristics.Value & 2) != 0;
            HasAudio = Method<NativeType>(_reader, 5)(_reader, AudioStream, 0, out nativeType) >= 0;
            Release(ref nativeType);
            if (!audio)
            {
                Check(Method<NativeType>(_reader, 5)(_reader, VideoStream, 0, out nativeType), "Read native video metadata");
                _sourceRotation = UInt32(nativeType, Rotation); Release(ref nativeType);
            }
            Check(Method<SelectStream>(_reader, 4)(_reader, AllStreams, 0), "Deselect media streams");
            Check(Method<SelectStream>(_reader, 4)(_reader, _streamIndex, 1), "Select media stream");
            Check(MFCreateMediaType(out type), "Create decoded media type");
            Set(type, MajorType, audio ? AudioType : VideoType);
            Set(type, Subtype, audio ? FloatAudio : Nv12);
            if (audio)
            {
                Set(type, Channels, 2); Set(type, SampleRate, 48000); Set(type, Bits, 32);
                Set(type, Align, 8); Set(type, BytesPerSecond, 384000);
            }
            Check(Method<SetType>(_reader, 7)(_reader, _streamIndex, 0, type), "Select decoded media format");
            if (!audio) RefreshVideoFormat();
            _counted = true; Interlocked.Increment(ref _activeSessions);
        }
        catch { Dispose(); throw; }
        finally { Release(ref attributes); Release(ref type); Release(ref nativeType); }
    }

    private Variant GetPresentation(Guid key)
    {
        Check(Method<Presentation>(_reader, 12)(_reader, MediaSource, key, out var value), "Read presentation metadata");
        try { return value; }
        finally { _ = PropVariantClear(ref value); }
    }

    private void RefreshVideoFormat()
    {
        Check(Method<CurrentType>(_reader, 6)(_reader, VideoStream, out var type), "Get decoded video format");
        try
        {
            var size = UInt64(type, FrameSize); var codedWidth = checked((int)(size >> 32)); var codedHeight = checked((int)(size & uint.MaxValue));
            if (codedWidth is < 1 or > 8192 || codedHeight is < 1 or > 8192 || (long)codedWidth * codedHeight * 4 > 64 * 1024 * 1024)
                throw new InvalidDataException("Decoded video exceeds the 64 MiB frame budget.");
            var width = codedWidth; var height = codedHeight; var x = 0; var y = 0; var aperture = new byte[16];
            if (Method<GetBlob>(type, 15)(type, DisplayAperture, aperture, 16, out var actual) >= 0 && actual == 16)
            {
                // MFVideoArea contains two 16.16 MFOffset values followed by SIZE.
                x = BitConverter.ToInt16(aperture, 2); y = BitConverter.ToInt16(aperture, 6);
                width = BitConverter.ToInt32(aperture, 8); height = BitConverter.ToInt32(aperture, 12);
                if (BitConverter.ToUInt16(aperture, 0) != 0 || BitConverter.ToUInt16(aperture, 4) != 0)
                    throw new NotSupportedException("Fractional video display apertures are not supported.");
            }
            if (width <= 0 || height <= 0 || x < 0 || y < 0 || (long)x + width > codedWidth || (long)y + height > codedHeight)
                throw new InvalidDataException("Invalid video display aperture.");
            var rotation = UInt32(type, Rotation, _sourceRotation);
            if (rotation is not (0 or 90 or 180 or 270)) throw new NotSupportedException("Video rotation must be 0, 90, 180 or 270 degrees.");
            var displayWidth = rotation is 90 or 270 ? height : width; var displayHeight = rotation is 90 or 270 ? width : height;
            if (Width != 0 && (displayWidth != Width || displayHeight != Height)) throw new InvalidDataException("Changing video dimensions during playback is not supported.");
            Width = displayWidth; Height = displayHeight; VisibleWidth = width; VisibleHeight = height; RotationDegrees = rotation;
            CodedWidth = codedWidth; CodedHeight = codedHeight; CropX = x; CropY = y;
            var rate = UInt64(type, FrameRate);
            FramesPerSecond = (rate & uint.MaxValue) == 0 ? 0 : (rate >> 32) / (double)(rate & uint.MaxValue);
            if (!double.IsFinite(FramesPerSecond) || FramesPerSecond is <= 0 or > 240) throw new InvalidDataException("Video frame rate must be positive and at most 240 fps.");
            YuvMatrix = UInt32(type, Matrix, Height > 576 ? 1u : 2u); NominalRange = UInt32(type, WindowsMediaNative.Range, 2);
            DefaultStride = unchecked((int)UInt32(type, Stride, (uint)((CodedWidth + 1) & ~1)));
            if (DefaultStride < CodedWidth) throw new InvalidDataException("Invalid NV12 video stride.");
        }
        finally { Release(ref type); }
    }

    internal void Seek(double seconds)
    {
        ObjectDisposedException.ThrowIf(_reader == 0, this);
        if (!CanSeek) throw new NotSupportedException("This media source does not support seeking.");
        var position = new Variant { Type = 20, Value = checked((long)Math.Round(seconds * 10_000_000d)) };
        Check(Method<Position>(_reader, 8)(_reader, Guid.Empty, position), "Seek video source");
    }

    internal bool Read(out nint sample, out long timestamp)
    {
        ObjectDisposedException.ThrowIf(_reader == 0, this);
        using var budget = LoopExecutionGuard.AllowIterations(4096);
        for (var i = 0; i < 4096; i++)
        {
            Check(Method<ReadSample>(_reader, 9)(_reader, _streamIndex, 0, out _, out var flags, out timestamp, out sample), "Decode media sample");
            if ((flags & 1) != 0) { Release(ref sample); throw new InvalidDataException("Media source reported a decode error."); }
            if ((flags & 0x20) != 0 && _streamIndex == VideoStream)
            {
                try { RefreshVideoFormat(); } catch { Release(ref sample); throw; }
            }
            if (sample != 0) return true;
            if ((flags & 2) != 0) return false;
        }
        throw new InvalidDataException("Media source returned too many empty samples.");
    }

    internal static byte[] ReadBuffer(nint sample, ref byte[] scratch, out bool hardware, out bool packed)
    {
        nint buffer = 0, buffer2d = 0, dxgi = 0;
        hardware = false; packed = false;
        try
        {
            Check(Method<BufferAt>(sample, 40)(sample, 0, out buffer), "Get decoded buffer");
            hardware = Method<Query>(buffer, 0)(buffer, DxgiBuffer, out dxgi) >= 0;
            if (Method<Query>(buffer, 0)(buffer, Buffer2D, out buffer2d) >= 0)
            {
                Check(Method<Out32>(buffer2d, 7)(buffer2d, out var length), "Get contiguous video size");
                EnsureBuffer(ref scratch, length);
                Check(Method<CopyBuffer>(buffer2d, 8)(buffer2d, scratch, length), "Read decoded video surface");
                packed = true; return scratch;
            }
            Release(ref buffer);
            Check(Method<OutPointer>(sample, 41)(sample, out buffer), "Join media sample buffers");
            Check(Method<BufferLock>(buffer, 3)(buffer, out var bytes, out _, out var size), "Lock decoded media");
            try { EnsureBuffer(ref scratch, size); Marshal.Copy(bytes, scratch, 0, checked((int)size)); }
            finally { Check(Method<Simple>(buffer, 4)(buffer), "Unlock decoded media"); }
            return scratch;
        }
        finally { Release(ref dxgi); Release(ref buffer2d); Release(ref buffer); }
    }

    private static void EnsureBuffer(ref byte[] buffer, uint length)
    {
        if (length is 0 or > 64 * 1024 * 1024) throw new InvalidDataException("Decoded media buffer exceeds its budget.");
        if (buffer.Length != length) buffer = new byte[checked((int)length)];
    }

    public void Dispose()
    {
        Release(ref _reader); Release(ref _byteStream); Release(ref _stream);
        Release(ref _manager); Release(ref _context); Release(ref _device);
        if (_mfInitialized) { _mfInitialized = false; _ = MFShutdown(); }
        if (_comInitialized) { _comInitialized = false; CoUninitialize(); }
        if (_counted) { _counted = false; Interlocked.Decrement(ref _activeSessions); }
    }
}
