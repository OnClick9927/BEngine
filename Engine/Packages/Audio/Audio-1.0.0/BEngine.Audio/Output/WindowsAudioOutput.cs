using System.Runtime.InteropServices;
using BEngine.Audio;

namespace BEngine.Platform;

public sealed class WindowsAudioOutputFactory : IAudioOutputFactory
{
    public IAudioOutput CreateDefault()
    {
        try { return new WindowsAudioOutput(); }
        catch (InvalidOperationException) { return OpenAlAudioOutputFactory.Shared.CreateDefault(); }
    }

    public IAudioOutput SilentOutput => OpenAlAudioOutputFactory.Shared.SilentOutput;
    public bool IsSystemOutput(IAudioOutput output) => output is WindowsAudioOutput ||
        OpenAlAudioOutputFactory.Shared.IsSystemOutput(output);
}

// The native device drains its buffers independently; managed submission stays on the engine frame loop.
internal sealed class WindowsAudioOutput : IIndependentAudioOutput, IAudioOutputDiagnostics, IDisposable
{
    public const int MaximumQueuedFrames = BufferedStereoPcmStream.Capacity; // 120 ms ceiling for frame jitter.
    private readonly Queue<NativeBuffer> _buffers;
    private readonly Stack<NativeBuffer> _freeBuffers;
    private readonly NativeBuffer[] _nativeBuffers;
    private readonly bool[] _preparedBuffers;
    private readonly object _gate = new();
    private readonly BufferedStereoPcmStream _stream = new(AudioRuntimeSettings.outputBufferMilliseconds * 48);
    private readonly short[] _pcm = new short[480];
    private readonly short[] _tailPcm = new short[(240 + BufferedStereoPcmStream.TailFrames) * 2];
    private int _nativeFrames;
    private int _targetNativeFrames = 2880;
    private long _firstSubmission;
    private bool _stopping, _primed, _disposing, _disposeFramesAccounted;
    private Exception? _pumpError;
    private IntPtr _device;
    private IntPtr _nativeMemory;
    private int _preparedBufferCount;
    // A tail owns 504 native frames until completion. Pump can only submit a
    // tail below 480 native frames, so at most one tail can be in flight even
    // when the device is paused or its completion flag is delayed.
    private NativeBuffer _tailBuffer;
    private bool _tailBufferAvailable;
    public int NativeResetCount { get; private set; }
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
    private static readonly int HeaderFlagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
    private const int HeaderDone = 1, HeaderPrepared = 2;
    public int sampleRate => 48000;
    public int channelCount => 2;
    public int PeakQueuedFrames { get; private set; }
    public int QueueResets => _stream.QueueResets;
    public float PeakSubmittedSample => _stream.PeakPcm;
    public int NonFiniteSamples => _stream.NonFiniteSamples;
    public int DroppedFrames => _stream.DroppedFrames;
    public int Underruns { get; private set; }
    public long? underrunEvents => Underruns;

    internal WindowsAudioOutput()
    {
        // The bounded queue also bounds the number of in-flight 5 ms blocks.
        // Prepare them once, before any audio is submitted, rather than asking
        // the driver to pin/unpin a fresh allocation for each block forever.
        int blockCount = (capacityFrames + 239) / 240;
        _buffers = new(blockCount + 1);
        _freeBuffers = new(blockCount);
        _nativeBuffers = new NativeBuffer[blockCount + 1];
        _preparedBuffers = new bool[blockCount + 1];
        var format = new WaveFormat { Format = 1, Channels = 2, SampleRate = 48000,
            AverageBytesPerSecond = 192000, BlockAlign = 4, BitsPerSample = 16 };
        Check(waveOutOpen(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
        try
        {
            int blockBytes = (int)HeaderSize + _pcm.Length * sizeof(short);
            _nativeMemory = Marshal.AllocHGlobal(blockCount * blockBytes + (int)HeaderSize + _tailPcm.Length * sizeof(short));
            for (int index = 0; index < _nativeBuffers.Length; index++)
            {
                bool tail = index == blockCount;
                var header = IntPtr.Add(_nativeMemory, index * blockBytes);
                var buffer = new NativeBuffer(header, IntPtr.Add(header, (int)HeaderSize), tail ? _tailPcm.Length / 2 : 240);
                _nativeBuffers[index] = buffer;
                Marshal.StructureToPtr(new WaveHeader { Data = buffer.Data, Length = (uint)(buffer.Frames * 4) }, header, false);
                Check(waveOutPrepareHeader(_device, header, HeaderSize));
                _preparedBuffers[index] = true;
                _preparedBufferCount++;
                if (tail) { _tailBuffer = buffer; _tailBufferAvailable = true; }
                else _freeBuffers.Push(buffer);
            }
            BTaskScheduler.Main.FrameAdvanced += Advance;
        }
        catch
        {
            for (int index = 0; index < _preparedBufferCount; index++)
                waveOutUnprepareHeader(_device, _nativeBuffers[index].Header, HeaderSize);
            waveOutClose(_device); _device = IntPtr.Zero;
            if (_nativeMemory != IntPtr.Zero) { Marshal.FreeHGlobal(_nativeMemory); _nativeMemory = IntPtr.Zero; }
            throw;
        }
    }

    public string backend => "Windows waveOut";
    public long submittedFrames { get; private set; }
    public long completedFrames { get; private set; }
    public long droppedFrames => Math.Min(submittedFrames, _stream.TotalDroppedFrames + _cancelledFrames);
    public int capacityFrames => _stream.CapacityFrames;
    public int queuedFrames { get { lock (_gate) { if (_device != IntPtr.Zero) ReleaseCompleted(); return _nativeFrames + _stream.Count; } } }
    public int availableFrames => Math.Max(0, capacityFrames - queuedFrames);
    public void SetExpectedSubmissionFrames(int frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        _targetNativeFrames = Math.Clamp(frames + 1920, Math.Min(2880, capacityFrames), Math.Max(Math.Min(2880, capacityFrames), capacityFrames - 1440));
    }
    public bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_device == IntPtr.Zero || _stopping, this);
            if (_pumpError is not null) throw new InvalidOperationException("Audio pump failed", _pumpError);
            ReleaseCompleted();
            if (_stream.Count == 0 && !_primed) _firstSubmission = System.Diagnostics.Stopwatch.GetTimestamp();
            var accepted = _stream.TryWrite(samples, capacityFrames - _nativeFrames, out acceptedFrames);
            submittedFrames += acceptedFrames; PeakQueuedFrames = Math.Max(PeakQueuedFrames, _stream.Count + _nativeFrames);
            Pump(); return accepted;
        }
    }
    private long _cancelledFrames;
    public int pendingBuffers { get { lock (_gate) { if (_device != IntPtr.Zero) ReleaseCompleted(); return _stopping ? 0 : _buffers.Count + (_stream.Count > 0 ? 1 : 0); } } }

    public void Submit(ReadOnlySpan<float> samples)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Audio output requires complete stereo frames.", nameof(samples));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_device == IntPtr.Zero || _stopping, this);
            if (samples.IsEmpty) return;
            submittedFrames += samples.Length / 2;
            // waveOut completion flags can lag the device clock by a system
            // timer quantum. Keep 40 ms beyond one producer frame, bounded to
            // 60–90 ms, so a 20 fps frame loop does not starve the native queue.
            SetExpectedSubmissionFrames(samples.Length / 2);
            if (_stream.Count == 0 && !_primed) _firstSubmission = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_pumpError is not null) throw new InvalidOperationException("Audio pump failed",_pumpError);
            ReleaseCompleted();
            _stream.Write(samples,capacityFrames-_nativeFrames);
            PeakQueuedFrames=Math.Max(PeakQueuedFrames,_stream.Count+_nativeFrames);
            Pump();
        }
    }
    private void Advance(BFrameTick _) => Pump();
    private void Pump()
    {
        try
        {
                lock(_gate)
                {
                    if(_stopping)return;
                    ReleaseCompleted();
                    // A small continuous device queue avoids resets and retains cue latency.
                    if (!_primed && (_stream.Count >= 2880 || _stream.Count > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_firstSubmission).TotalMilliseconds >= 45)) _primed = true;
                    while(_primed && _nativeFrames<_targetNativeFrames && _stream.Count>=240)
                    {
                        _stream.Read(_pcm);WritePcm(_pcm,240);
                    }
                    if(_primed && _nativeFrames<480 && _stream.Count<240)
                    {
                        // Drain a smooth tail only when starvation is imminent;
                        // never insert a silent pad between normal frame submissions.
                        _stream.Read(_tailPcm);WritePcm(_tailPcm,_tailPcm.Length/2);_primed=false;Underruns++;
                    }
                }
        }
        catch(Exception e){lock(_gate){_pumpError=e;}}
    }
    private void WritePcm(short[] pcm,int frames)
    {
        bool tail = frames != 240;
        if (tail ? !_tailBufferAvailable : _freeBuffers.Count == 0)
            throw new InvalidOperationException("Windows audio native buffer pool is exhausted");
        var buffer = tail ? _tailBuffer : _freeBuffers.Pop();
        if (tail) _tailBufferAvailable = false;
        try
        {
            Marshal.Copy(pcm,0,buffer.Data,pcm.Length);
            // Only completed blocks return to the pool. Preserve the prepared
            // header and the driver's reserved fields when submitting it again.
            Marshal.WriteInt32(buffer.Header,HeaderFlagsOffset,HeaderPrepared);
            Check(waveOutWrite(_device,buffer.Header,HeaderSize));
            _nativeFrames+=frames;_buffers.Enqueue(buffer);
        }
        catch { ReturnBuffer(buffer); throw; }
    }

    private void ReleaseCompleted()
    {
        // waveOut returns these non-looping blocks in submission order. A
        // single flag read normally suffices when no block completed this frame.
        while (_buffers.TryPeek(out var buffer))
        {
            if ((Marshal.ReadInt32(buffer.Header,HeaderFlagsOffset) & HeaderDone) == 0) break;
            _buffers.Dequeue(); _nativeFrames -= buffer.Frames;
            completedFrames = Math.Min(submittedFrames - droppedFrames, completedFrames + buffer.Frames);
            ReturnBuffer(buffer);
        }
    }

    private void ReturnBuffer(NativeBuffer buffer)
    {
        if (buffer.Frames == 240) _freeBuffers.Push(buffer);
        else _tailBufferAvailable = true;
    }

    public void Dispose() => DisposeNative(waveOutReset,waveOutUnprepareHeader,waveOutClose);

    private void DisposeNative(Func<IntPtr,uint> reset,Func<IntPtr,IntPtr,uint,uint> unprepare,Func<IntPtr,uint> close)
    {
        lock(_gate) {if(_device==IntPtr.Zero || _disposing)return;_stopping=true;_disposing=true;}
        BTaskScheduler.Main.FrameAdvanced -= Advance;
        lock(_gate)
        {
            List<Exception>? failures=null;
            bool Succeeded(uint result,string operation)
            {
                if(result==0)return true;
                (failures??=[]).Add(new InvalidOperationException($"Windows audio {operation} MMRESULT {result}"));return false;
            }
            try
            {
                // Account cancellation once even if a failed native cleanup is
                // retried. Never permit new submissions during these retries.
                if(!_disposeFramesAccounted){_cancelledFrames+=_stream.Count+_nativeFrames;_disposeFramesAccounted=true;}
                var played=completedFrames;
                if(Succeeded(reset(_device),"reset"))NativeResetCount++;
                ReleaseCompleted();completedFrames=played;
                // A failed reset/unprepare must not prevent other safe cleanup
                // attempts. Remember which headers still require unpreparing.
                for(int index=0;index<_nativeBuffers.Length;index++)
                    if(_preparedBuffers[index] && Succeeded(unprepare(_device,_nativeBuffers[index].Header,HeaderSize),"unprepare"))
                    {_preparedBuffers[index]=false;_preparedBufferCount--;}
                if(Succeeded(close(_device),"close"))
                {
                    _device=IntPtr.Zero;
                    // Only a successfully closed device guarantees the driver
                    // cannot retain pointers into this slab. Otherwise retain
                    // all storage and allow a later Dispose to retry cleanup.
                    Marshal.FreeHGlobal(_nativeMemory);_nativeMemory=IntPtr.Zero;
                    _preparedBufferCount=0;_buffers.Clear();_freeBuffers.Clear();_tailBufferAvailable=false;
                    Array.Clear(_nativeBuffers);Array.Clear(_preparedBuffers);
                }
            }
            finally{_disposing=false;}
            if(failures is {Count:>0})throw new AggregateException("Windows audio cleanup failed",failures);
        }
    }

    private static void Check(uint result)
    { if (result != 0) throw new InvalidOperationException("Windows audio MMRESULT " + result); }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat
    { public ushort Format, Channels; public uint SampleRate, AverageBytesPerSecond; public ushort BlockAlign, BitsPerSample, ExtraSize; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader
    { public IntPtr Data; public uint Length, BytesRecorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    private readonly record struct NativeBuffer(IntPtr Header, IntPtr Data, int Frames);
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);
}
