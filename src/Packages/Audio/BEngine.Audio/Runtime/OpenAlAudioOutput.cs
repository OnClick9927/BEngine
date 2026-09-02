using System.Buffers;
using System.Runtime.InteropServices;

namespace BEngine.Audio;

public sealed class OpenAlAudioOutput : IAudioOutput, IDisposable
{
    private const int FormatMono16 = 0x1101;
    private const int FormatStereo16 = 0x1103;
    private const int BuffersProcessed = 0x1016;
    private const int SourceState = 0x1010;
    private const int Playing = 0x1012;
    private readonly nint _library;
    private readonly nint _device;
    private readonly nint _context;
    private readonly uint _source;
    private readonly Queue<uint> _available = [];
    private readonly HashSet<uint> _buffers = [];
    private readonly AlBufferData _bufferData;
    private readonly AlSourceQueueBuffers _queueBuffers;
    private readonly AlSourceUnqueueBuffers _unqueueBuffers;
    private readonly AlGetSourceI _getSourceI;
    private readonly AlSourcePlay _sourcePlay;
    private readonly AlDeleteSources _deleteSources;
    private readonly AlDeleteBuffers _deleteBuffers;
    private readonly AlcMakeContextCurrent _makeContextCurrent;
    private readonly AlcDestroyContext _destroyContext;
    private readonly AlcCloseDevice _closeDevice;
    private bool _disposed;

    public int sampleRate { get; }
    public int channelCount { get; }

    private OpenAlAudioOutput(nint library, int sampleRate, int channelCount)
    {
        _library = library;
        this.sampleRate = sampleRate;
        this.channelCount = channelCount;
        var openDevice = Load<AlcOpenDevice>(library, "alcOpenDevice");
        var createContext = Load<AlcCreateContext>(library, "alcCreateContext");
        _makeContextCurrent = Load<AlcMakeContextCurrent>(library, "alcMakeContextCurrent");
        _destroyContext = Load<AlcDestroyContext>(library, "alcDestroyContext");
        _closeDevice = Load<AlcCloseDevice>(library, "alcCloseDevice");
        var generateSources = Load<AlGenSources>(library, "alGenSources");
        var generateBuffers = Load<AlGenBuffers>(library, "alGenBuffers");
        _bufferData = Load<AlBufferData>(library, "alBufferData");
        _queueBuffers = Load<AlSourceQueueBuffers>(library, "alSourceQueueBuffers");
        _unqueueBuffers = Load<AlSourceUnqueueBuffers>(library, "alSourceUnqueueBuffers");
        _getSourceI = Load<AlGetSourceI>(library, "alGetSourcei");
        _sourcePlay = Load<AlSourcePlay>(library, "alSourcePlay");
        _deleteSources = Load<AlDeleteSources>(library, "alDeleteSources");
        _deleteBuffers = Load<AlDeleteBuffers>(library, "alDeleteBuffers");

        _device = openDevice(0);
        _context = _device == 0 ? 0 : createContext(_device, 0);
        try
        {
            if (_device == 0) throw new InvalidOperationException("OpenAL could not open the default output device.");
            if (_context == 0 || !_makeContextCurrent(_context))
                throw new InvalidOperationException("OpenAL could not create an audio context.");
            generateSources(1, out _source);
            if (_source == 0) throw new InvalidOperationException("OpenAL could not create a streaming source.");
            for (var index = 0; index < 4; index++)
            {
                generateBuffers(1, out var buffer);
                if (buffer == 0) throw new InvalidOperationException("OpenAL could not create a streaming buffer.");
                _buffers.Add(buffer);
                _available.Enqueue(buffer);
            }
        }
        catch
        {
            if (_context != 0)
            {
                _makeContextCurrent(0);
                _destroyContext(_context);
            }
            if (_device != 0) _closeDevice(_device);
            throw;
        }
    }

    public static bool TryCreate(out OpenAlAudioOutput? output, int sampleRate = 48000,
        int channelCount = 2)
    {
        output = null;
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(channelCount), "OpenAL output supports mono or stereo.");
        if (!TryLoadLibrary(out var library)) return false;
        try
        {
            output = new OpenAlAudioOutput(library, sampleRate, channelCount);
            return true;
        }
        catch
        {
            if (output is not null) output.Dispose();
            else NativeLibrary.Free(library);
            output = null;
            return false;
        }
    }

    public void Submit(ReadOnlySpan<float> interleavedSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % channelCount != 0) return;
        ReclaimProcessedBuffers();
        if (!_available.TryDequeue(out var buffer)) return;

        var pcm = ArrayPool<short>.Shared.Rent(interleavedSamples.Length);
        for (var index = 0; index < interleavedSamples.Length; index++)
            pcm[index] = (short)Math.Round(Math.Clamp(interleavedSamples[index], -1f, 1f) * short.MaxValue);
        var handle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        try
        {
            _bufferData(buffer, channelCount == 1 ? FormatMono16 : FormatStereo16,
                handle.AddrOfPinnedObject(), checked(interleavedSamples.Length * sizeof(short)), sampleRate);
        }
        finally
        {
            handle.Free();
            ArrayPool<short>.Shared.Return(pcm);
        }
        _queueBuffers(_source, 1, ref buffer);
        _getSourceI(_source, SourceState, out var state);
        if (state != Playing) _sourcePlay(_source);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_context != 0) _makeContextCurrent(_context);
            if (_source != 0)
            {
                var source = _source;
                _deleteSources(1, ref source);
            }
            foreach (var value in _buffers)
            {
                var buffer = value;
                _deleteBuffers(1, ref buffer);
            }
            if (_context != 0)
            {
                _makeContextCurrent(0);
                _destroyContext(_context);
            }
            if (_device != 0) _closeDevice(_device);
        }
        finally { NativeLibrary.Free(_library); }
        GC.SuppressFinalize(this);
    }

    private void ReclaimProcessedBuffers()
    {
        _getSourceI(_source, BuffersProcessed, out var processed);
        while (processed-- > 0)
        {
            _unqueueBuffers(_source, 1, out var buffer);
            if (_buffers.Contains(buffer)) _available.Enqueue(buffer);
        }
    }

    private static T Load<T>(nint library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static bool TryLoadLibrary(out nint library)
    {
        foreach (var candidate in OperatingSystem.IsWindows()
                     ? new[] { "openal32.dll", "soft_oal.dll" }
                     : OperatingSystem.IsMacOS()
                         ? new[] { "/System/Library/Frameworks/OpenAL.framework/OpenAL", "libopenal.dylib" }
                         : new[] { "libopenal.so.1", "libopenal.so" })
            if (NativeLibrary.TryLoad(candidate, out library)) return true;
        library = 0;
        return false;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint AlcOpenDevice(nint name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint AlcCreateContext(nint device, nint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool AlcMakeContextCurrent(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlcDestroyContext(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool AlcCloseDevice(nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlGenSources(int count, out uint source);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlDeleteSources(int count, ref uint source);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlGenBuffers(int count, out uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlDeleteBuffers(int count, ref uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlBufferData(uint buffer, int format, nint data, int size, int frequency);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlSourceQueueBuffers(uint source, int count, ref uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlSourceUnqueueBuffers(uint source, int count, out uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlGetSourceI(uint source, int parameter, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlSourcePlay(uint source);
}
