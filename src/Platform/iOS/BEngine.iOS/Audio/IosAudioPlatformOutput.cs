using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

internal sealed class IosAudioPlatformOutputFactory : IPlatformAudioOutputFactory
{
    internal static IosAudioPlatformOutputFactory Shared { get; } = new();

    public IPlatformAudioOutput? CreateDefault(int sampleRate, int channelCount)
    {
        if (!OperatingSystem.IsIOS()) return null;
        try { return new IosAudioPlatformOutput(sampleRate, channelCount); }
        catch { return null; }
    }
}

internal sealed unsafe partial class IosAudioPlatformOutput : IPlatformAudioOutput
{
    private const int BufferCount = 8;
    private const int BufferFrames = 2048;
    private readonly object _gate = new();
    private readonly Queue<nint> _available = new();
    private readonly Dictionary<nint, int> _queuedFrames = [];
    private readonly nint[] _buffers = new nint[BufferCount];
    private GCHandle _self;
    private nint _queue;
    private bool _started;
    private bool _disposed;
    private long _submittedFrames;
    private long _completedFrames;
    private long _droppedFrames;

    internal IosAudioPlatformOutput(int sampleRate, int channelCount)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount <= 0) throw new ArgumentOutOfRangeException(nameof(channelCount));
        var runLoop = AudioQueueNative.MainRunLoop();
        if (runLoop == 0 || AudioQueueNative.CurrentRunLoop() != runLoop)
            throw new InvalidOperationException("iOS audio output must be created on the engine's main thread.");
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        var format = new AudioStreamBasicDescription
        {
            SampleRate = sampleRate,
            FormatId = 0x6C70636D,
            FormatFlags = 1 | 8,
            BytesPerPacket = (uint)(channelCount * sizeof(float)),
            FramesPerPacket = 1,
            BytesPerFrame = (uint)(channelCount * sizeof(float)),
            ChannelsPerFrame = (uint)channelCount,
            BitsPerChannel = 32
        };
        _self = GCHandle.Alloc(this);
        try
        {
            ThrowIfFailed(AudioQueueNative.NewOutput(ref format,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnBufferComplete,
                GCHandle.ToIntPtr(_self), runLoop, 0, 0, out _queue), "create output queue");
            var capacity = checked((uint)(BufferFrames * channelCount * sizeof(float)));
            for (var index = 0; index < _buffers.Length; index++)
            {
                ThrowIfFailed(AudioQueueNative.AllocateBuffer(_queue, capacity, out _buffers[index]),
                    "allocate output buffer");
                _available.Enqueue(_buffers[index]);
            }
            IosAudioOutputLifecycle.Register(this);
        }
        catch
        {
            if (_queue != 0) AudioQueueNative.Dispose(_queue, true);
            if (_self.IsAllocated) _self.Free();
            throw;
        }
    }

    public int SampleRate { get; }
    public int ChannelCount { get; }
    public string Backend => "iOS AudioQueue";
    public long SubmittedFrames => Interlocked.Read(ref _submittedFrames);
    public long CompletedFrames => Interlocked.Read(ref _completedFrames);
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);
    public int PendingBuffers { get { lock (_gate) return _queuedFrames.Count; } }

    public void Submit(ReadOnlySpan<float> interleavedSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % ChannelCount != 0) return;
        var offset = 0;
        while (offset < interleavedSamples.Length)
        {
            nint buffer;
            lock (_gate)
            {
                if (!_available.TryDequeue(out buffer))
                {
                    Interlocked.Add(ref _droppedFrames,
                        (interleavedSamples.Length - offset) / ChannelCount);
                    return;
                }
            }
            var samples = Math.Min(BufferFrames * ChannelCount, interleavedSamples.Length - offset);
            var header = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
            fixed (float* source = interleavedSamples.Slice(offset, samples))
                Buffer.MemoryCopy(source, (void*)header.AudioData,
                    header.AudioDataBytesCapacity, checked(samples * sizeof(float)));
            Marshal.WriteInt32(buffer,
                Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.AudioDataByteSize)).ToInt32(),
                samples * sizeof(float));
            var frames = samples / ChannelCount;
            lock (_gate) _queuedFrames[buffer] = frames;
            var result = AudioQueueNative.EnqueueBuffer(_queue, buffer, 0, 0);
            if (result != 0)
            {
                lock (_gate) { _queuedFrames.Remove(buffer); _available.Enqueue(buffer); }
                Interlocked.Add(ref _droppedFrames, frames);
                return;
            }
            Interlocked.Add(ref _submittedFrames, frames);
            offset += samples;
            if (_started) continue;
            ThrowIfFailed(AudioQueueNative.Start(_queue, 0), "start output queue");
            _started = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var queue = Interlocked.Exchange(ref _queue, 0);
        if (queue != 0)
        {
            AudioQueueNative.Stop(queue, true);
            AudioQueueNative.Dispose(queue, true);
        }
        IosAudioOutputLifecycle.Unregister(this);
        if (_self.IsAllocated) _self.Free();
        GC.SuppressFinalize(this);
    }

    internal void SuspendForInterruption()
    {
        var queue = Volatile.Read(ref _queue);
        if (queue != 0 && _started)
            ThrowIfFailed(AudioQueueNative.Pause(queue), "pause output queue");
    }

    internal void ResumeAfterInterruption()
    {
        var queue = Volatile.Read(ref _queue);
        if (queue != 0 && _started)
            ThrowIfFailed(AudioQueueNative.Start(queue, 0), "resume output queue");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBufferComplete(nint userData, nint queue, nint buffer)
    {
        try
        {
            if (userData == 0) return;
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is not IosAudioPlatformOutput output || output._disposed) return;
            var frames = 0;
            lock (output._gate)
            {
                if (output._queuedFrames.Remove(buffer, out var completed)) frames = completed;
                output._available.Enqueue(buffer);
            }
            if (frames > 0) Interlocked.Add(ref output._completedFrames, frames);
        }
        catch { }
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result == 0) return;
        throw new InvalidOperationException($"AudioQueue could not {operation}: OSStatus {result}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        internal double SampleRate;
        internal uint FormatId;
        internal uint FormatFlags;
        internal uint BytesPerPacket;
        internal uint FramesPerPacket;
        internal uint BytesPerFrame;
        internal uint ChannelsPerFrame;
        internal uint BitsPerChannel;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioQueueBuffer
    {
        internal uint AudioDataBytesCapacity;
        internal nint AudioData;
        internal uint AudioDataByteSize;
        internal nint UserData;
        internal uint PacketDescriptionCapacity;
        internal nint PacketDescriptions;
        internal uint PacketDescriptionCount;
    }

    private static partial class AudioQueueNative
    {
        private const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [LibraryImport(CoreFoundation, EntryPoint = "CFRunLoopGetMain")]
        internal static partial nint MainRunLoop();
        [LibraryImport(CoreFoundation, EntryPoint = "CFRunLoopGetCurrent")]
        internal static partial nint CurrentRunLoop();
        [LibraryImport(Library, EntryPoint = "AudioQueueNewOutput")]
        internal static partial int NewOutput(ref AudioStreamBasicDescription format, nint callback,
            nint userData, nint callbackRunLoop, nint callbackRunLoopMode, uint flags, out nint queue);
        [LibraryImport(Library, EntryPoint = "AudioQueueAllocateBuffer")]
        internal static partial int AllocateBuffer(nint queue, uint capacity, out nint buffer);
        [LibraryImport(Library, EntryPoint = "AudioQueueEnqueueBuffer")]
        internal static partial int EnqueueBuffer(nint queue, nint buffer,
            uint packetDescriptionCount, nint packetDescriptions);
        [LibraryImport(Library, EntryPoint = "AudioQueueStart")]
        internal static partial int Start(nint queue, nint startTime);
        [LibraryImport(Library, EntryPoint = "AudioQueuePause")]
        internal static partial int Pause(nint queue);
        [LibraryImport(Library, EntryPoint = "AudioQueueStop")]
        internal static partial int Stop(nint queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
        [LibraryImport(Library, EntryPoint = "AudioQueueDispose")]
        internal static partial int Dispose(nint queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
    }
}
