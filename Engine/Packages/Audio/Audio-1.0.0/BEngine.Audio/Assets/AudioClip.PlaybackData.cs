namespace BEngine.Audio;

// Captured on the engine thread. The mixer owns independent readers and never reads
// a BObject, mutable AudioClip, resource registry or scene on its audio thread.
internal sealed class AudioPlaybackData(float[] pcm, Func<AudioSampleReader>? factory,
    int samples, int channels, int frequency)
{
    internal readonly float[] Pcm = pcm;
    internal readonly int Samples = samples, Channels = channels, Frequency = frequency;
    private int _valid = 1;
    internal bool IsValid => Volatile.Read(ref _valid) != 0;
    internal void Invalidate() => Volatile.Write(ref _valid, 0);
    internal AudioSampleReader OpenReader() => factory is not null ? factory() : new PcmReader(Pcm, Channels, Frequency);

    private sealed class PcmReader(float[] data, int channelCount, int sampleRate) : AudioSampleReader
    {
        private int _position;
        public override int samples => data.Length / channelCount;
        public override int channels => channelCount;
        public override int frequency => sampleRate;
        public override int position => _position;
        protected override int ReadCore(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, (samples - _position) * channels);
            data.AsSpan(_position * channels, read).CopyTo(buffer.AsSpan(offset));
            _position += read / channels; return read;
        }
        protected override void SeekCore(int frame) => _position = frame;
    }
}

public sealed partial class AudioClip
{
    private AudioPlaybackData? _playbackData;
    internal bool TryCapturePlaybackData(out AudioPlaybackData data)
    {
        // Procedural factories/callbacks may access gameplay and retain their
        // existing main-thread contract. Do not silently invoke them on a worker.
        if (isDestroyed || loadState != AudioDataLoadState.Loaded || _factory is not null)
        { data = null!; return false; }
        data = _playbackData ??= new(_pcm, HasDecodedPcm ? null : SourceReaderFactory(), samples, channels, frequency);
        return true;
    }
    private bool InvalidatePlaybackData(bool stop = false)
    {
        if (_playbackData is not { } data) return true;
        // Drain commands and close readers before changing mutable PCM or releasing
        // its reservation. The acknowledgement is bounded; callers can retry
        // instead of blocking a gameplay frame behind a stalled device.
        if (!AudioIndependentClock.Invalidate(data, stop)) return false;
        data.Invalidate();
        _playbackData = null;
        return true;
    }
}
