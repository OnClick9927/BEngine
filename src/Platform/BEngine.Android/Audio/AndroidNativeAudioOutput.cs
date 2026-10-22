using System.Runtime.InteropServices;
using System.Threading;

namespace BEngine.Platform;

internal sealed class AndroidNativeAudioOutputFactory : IPlatformAudioOutputFactory
{
    internal static AndroidNativeAudioOutputFactory Shared { get; } = new();
    public IPlatformAudioOutput? CreateDefault(int sampleRate, int channelCount) =>
        AndroidNativeAudioOutput.TryCreate(out var output, sampleRate, channelCount) ? output : null;
}

internal sealed unsafe class AndroidNativeAudioOutput : IPlatformBufferedAudioOutput
{
    private static readonly object InstancesGate = new();
    private static readonly HashSet<AndroidNativeAudioOutput> Instances = [];
    private static bool Suspended;
    private const int CallbackFrames = 512;
    private readonly object _gate = new();
    private readonly object _pumpGate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly float[] _samples;
    private readonly float[] _block;
    private readonly Thread _pumpThread;
    private nint _stream;
    private int _readOffset;
    private int _queuedSamples;
    private volatile bool _disposed;
    private long _submittedFrames;
    private long _completedFrames;
    private long _droppedFrames;
    private long _firstSubmission;
    private bool _started, _suspended, _errorReported;

    private AndroidNativeAudioOutput(int requestedSampleRate, int requestedChannelCount)
    {
        var result = AAudioNative.CreateStreamBuilder(out var builder);
        ThrowIfFailed(result, "create stream builder");
        try
        {
            AAudioNative.SetDirection(builder, 0);
            AAudioNative.SetFormat(builder, 2);
            AAudioNative.SetSampleRate(builder, requestedSampleRate);
            AAudioNative.SetChannelCount(builder, requestedChannelCount);
            AAudioNative.SetSharingMode(builder, 1);
            AAudioNative.SetPerformanceMode(builder, 12);
            result = AAudioNative.OpenStream(builder, out _stream);
            ThrowIfFailed(result, "open output stream");
        }
        catch
        {
            if (_stream != 0) AAudioNative.Close(_stream);
            throw;
        }
        finally { AAudioNative.DeleteStreamBuilder(builder); }

        SampleRate = Math.Max(1, AAudioNative.GetSampleRate(_stream));
        ChannelCount = Math.Max(1, AAudioNative.GetChannelCount(_stream));
        // A mobile render frame can be delayed by a surface resize, GC or a
        // background transition.  Keep a larger native queue than desktop and
        // pump it independently of the game/render thread so those stalls do
        // not become audible gaps.
        var bufferMilliseconds = Math.Clamp(AndroidRuntimeSettings.audioBufferMilliseconds, 60, 2000);
        _samples = new float[checked(SampleRate * ChannelCount * bufferMilliseconds / 1000)];
        _block = new float[checked(CallbackFrames * ChannelCount)];
        lock (InstancesGate) { _suspended = Suspended; Instances.Add(this); }
        _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "BEngine Android audio" };
        _pumpThread.Start();
    }

    public int SampleRate { get; }
    public int ChannelCount { get; }
    public string Backend => "Android AAudio";
    public long SubmittedFrames => Interlocked.Read(ref _submittedFrames);
    public long CompletedFrames { get { lock (_pumpGate) return PlayedFrames(); } }
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);
    public int CapacityFrames => _samples.Length / ChannelCount;
    public int QueuedFrames
    { get { lock (_pumpGate) { var native = _completedFrames - PlayedFrames(); lock (_gate) return _queuedSamples / ChannelCount + (int)native; } } }
    public int AvailableFrames => Math.Max(0, CapacityFrames - QueuedFrames);
    public void SetExpectedSubmissionFrames(int frames) { if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames)); }
    public bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames)
    {
        acceptedFrames = Math.Min(samples.Length / ChannelCount, AvailableFrames);
        if (acceptedFrames > 0) Submit(samples[..(acceptedFrames * ChannelCount)]);
        return acceptedFrames == samples.Length / ChannelCount;
    }
    public int PendingBuffers
    {
        get => (QueuedFrames + CallbackFrames - 1) / CallbackFrames;
    }

    public static bool TryCreate(out AndroidNativeAudioOutput? output, int SampleRate = 48000, int ChannelCount = 2)
    {
        output = null;
        if (!OperatingSystem.IsAndroid()) return false;
        try { output = new AndroidNativeAudioOutput(SampleRate, ChannelCount); return true; }
        catch (Exception exception)
        {
            Debug.LogWarning($"AAudio output is unavailable: {exception.Message}");
            output?.Dispose();
            output = null;
            return false;
        }
    }

    public void Submit(ReadOnlySpan<float> interleavedSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % ChannelCount != 0) return;
        lock (_gate)
        {
            if (_firstSubmission == 0) _firstSubmission = System.Diagnostics.Stopwatch.GetTimestamp();
            if (interleavedSamples.Length > _samples.Length - _queuedSamples)
            {
                Interlocked.Add(ref _droppedFrames, interleavedSamples.Length / ChannelCount);
                return;
            }
            var writeOffset = (_readOffset + _queuedSamples) % _samples.Length;
            var first = Math.Min(interleavedSamples.Length, _samples.Length - writeOffset);
            interleavedSamples[..first].CopyTo(_samples.AsSpan(writeOffset));
            interleavedSamples[first..].CopyTo(_samples);
            _queuedSamples += interleavedSamples.Length;
        }
        Interlocked.Add(ref _submittedFrames, interleavedSamples.Length / ChannelCount);
        _wake.Set();
        // Keep native AAudio writes off the game/render thread. Some vendor
        // implementations can briefly block even with a zero timeout while
        // the mixer is being rerouted (Bluetooth, app resume, phone calls).
        // Calling Pump here used to turn that short audio stall into a frozen
        // frame or a deadlock with the lifecycle path. The dedicated pump
        // thread is already woken above and owns all native writes.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Remove before taking _pumpGate: lifecycle takes InstancesGate then
        // _pumpGate, so reversing that order during shutdown can deadlock.
        lock (InstancesGate) Instances.Remove(this);
        _wake.Set();
        var stopped = Thread.CurrentThread == _pumpThread || _pumpThread.Join(500);
        lock (_pumpGate)
        {
            var stream = _stream;
            if (stream != 0)
            {
                AAudioNative.RequestStop(stream);
                AAudioNative.Close(stream);
                _stream = 0;
            }
        }
        if (stopped) _wake.Dispose();
        GC.SuppressFinalize(this);
    }

    private void PumpLoop()
    {
        while (!_disposed)
        {
            Pump();
            // AAudio's non-blocking write is intentionally paired with a short
            // wait.  This keeps the mixer responsive without burning a core.
            _wake.WaitOne(3);
        }
    }

    private void Pump()
    {
        if (_disposed || _stream == 0) return;
        lock (_pumpGate)
        {
            if (_disposed || _suspended || _stream == 0) return;
            if (!_started)
            {
                lock (_gate)
                    if (_queuedSamples == 0 || _queuedSamples / ChannelCount < SampleRate * 80 / 1000 &&
                        System.Diagnostics.Stopwatch.GetElapsedTime(_firstSubmission).TotalMilliseconds < 80) return;
                var result = AAudioNative.RequestStart(_stream);
                if (result < 0) { ReportError(result); return; }
                _started = true;
            }
            for (var batch = 0; batch < 32; batch++)
            {
                int frames, count, readOffset;
                lock (_gate)
                {
                    if (_queuedSamples <= 0) return;
                    frames = Math.Min(CallbackFrames, _queuedSamples / ChannelCount);
                    count = frames * ChannelCount;
                    readOffset = _readOffset;
                    var first = Math.Min(count, _samples.Length - readOffset);
                    _samples.AsSpan(readOffset, first).CopyTo(_block);
                    _samples.AsSpan(0, count - first).CopyTo(_block.AsSpan(first));
                }
                int written;
                fixed (float* pointer = _block)
                    written = AAudioNative.Write(_stream, (nint)pointer, frames, 0);
                if (written < 0) { ReportError(written); return; }
                if (written == 0) return;
                var consumed = written * ChannelCount;
                lock (_gate)
                {
                    // Only this pump owns the read cursor; Submit can append
                    // concurrently but never removes queued samples.
                    _readOffset = (readOffset + consumed) % _samples.Length;
                    _queuedSamples = Math.Max(0, _queuedSamples - consumed);
                }
                Interlocked.Add(ref _completedFrames, written);
            }
        }
    }

    private long PlayedFrames() => _stream == 0 ? _completedFrames :
        Math.Clamp(AAudioNative.GetFramesRead(_stream), 0, _completedFrames);

    private void ReportError(int result)
    {
        if (_errorReported) return;
        _errorReported = true;
        Debug.LogWarning($"AAudio output failed: {ResultText(result)}.");
    }

    internal static void SetSuspended(bool suspended)
    {
        lock (InstancesGate)
        {
            Suspended = suspended;
            foreach (var output in Instances)
                lock (output._pumpGate)
                {
                    output._suspended = suspended;
                    if (output._disposed || output._stream == 0 || !output._started) continue;
                    if (suspended) AAudioNative.RequestPause(output._stream);
                    else { AAudioNative.RequestStart(output._stream); output._wake.Set(); }
                }
        }
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result >= 0) return;
        throw new InvalidOperationException($"AAudio could not {operation}: {ResultText(result)}.");
    }

    private static string ResultText(int result) =>
        Marshal.PtrToStringAnsi(AAudioNative.ConvertResultToText(result)) ?? result.ToString();
}

internal static partial class AAudioNative
{
    private const string Library = "aaudio";
    [LibraryImport(Library, EntryPoint = "AAudio_createStreamBuilder")]
    internal static partial int CreateStreamBuilder(out nint builder);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_delete")]
    internal static partial void DeleteStreamBuilder(nint builder);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_setDirection")]
    internal static partial void SetDirection(nint builder, int direction);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_setFormat")]
    internal static partial void SetFormat(nint builder, int format);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_setSampleRate")]
    internal static partial void SetSampleRate(nint builder, int SampleRate);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_setChannelCount")]
    internal static partial void SetChannelCount(nint builder, int ChannelCount);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_setSharingMode")]
    internal static partial void SetSharingMode(nint builder, int sharingMode);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_setPerformanceMode")]
    internal static partial void SetPerformanceMode(nint builder, int performanceMode);
    [LibraryImport(Library, EntryPoint = "AAudioStreamBuilder_openStream")]
    internal static partial int OpenStream(nint builder, out nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_requestStart")]
    internal static partial int RequestStart(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_requestStop")]
    internal static partial int RequestStop(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_requestPause")]
    internal static partial int RequestPause(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_close")]
    internal static partial int Close(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_write")]
    internal static partial int Write(nint stream, nint buffer, int frames, long timeoutNanoseconds);
    [LibraryImport(Library, EntryPoint = "AAudioStream_getSampleRate")]
    internal static partial int GetSampleRate(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_getChannelCount")]
    internal static partial int GetChannelCount(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudioStream_getFramesRead")]
    internal static partial long GetFramesRead(nint stream);
    [LibraryImport(Library, EntryPoint = "AAudio_convertResultToText")]
    internal static partial nint ConvertResultToText(int result);
}
