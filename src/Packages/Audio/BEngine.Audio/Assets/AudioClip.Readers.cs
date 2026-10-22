namespace BEngine.Audio;

public sealed partial class AudioClip
{
    private sealed class MemoryReader(float[] data, int channelCount, int sampleRate) : AudioSampleReader
    {
        private int _position;
        public override int channels => channelCount;
        public override int frequency => sampleRate;
        public override int samples => data.Length / channels;
        public override int position => _position;
        protected override int ReadCore(float[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, (long)(samples - _position) * channels);
            data.AsSpan(_position * channels, read).CopyTo(buffer.AsSpan(offset)); _position += read / channels; return read;
        }
        protected override void SeekCore(int frame) => _position = frame;
    }
    private sealed class OwnedReader(AudioClip owner, AudioSampleReader reader) : AudioSampleReader
    {
        public override bool hasPendingSamples => reader.hasPendingSamples;
        public override int channels => reader.channels;
        public override int frequency => reader.frequency;
        public override int samples => reader.samples;
        public override int position => reader.position;
        protected override int ReadCore(float[] buffer, int offset, int count) => reader.Read(buffer, offset, count);
        protected override void SeekCore(int frame) => reader.Seek(frame);
        protected override void DisposeCore() { try { reader.Dispose(); } finally { owner._readers.Remove(this); } }
    }
    private sealed class TransformReader(AudioSampleReader reader, bool mono, float gain) : AudioSampleReader
    {
        public override bool hasPendingSamples => reader.hasPendingSamples;
        private readonly float[] _buffer = new float[4096 * reader.channels];
        public override int channels => mono ? 1 : reader.channels;
        public override int frequency => reader.frequency;
        public override int samples => reader.samples;
        public override int position => reader.position;
        protected override int ReadCore(float[] buffer, int offset, int count)
        {
            var inputChannels = reader.channels;
            var read = reader.Read(_buffer, 0, Math.Min(4096, count / channels) * inputChannels); var frames = read / inputChannels;
            using var budget = LoopExecutionGuard.AllowIterations(read + 1);
            for (var frame = 0; frame < frames; frame++)
            {
                if (mono)
                {
                    float sum = 0;
                    for (var channel = 0; channel < inputChannels; channel++) sum += _buffer[frame * inputChannels + channel];
                    buffer[offset + frame] = sum / inputChannels * gain;
                }
                else for (var channel = 0; channel < inputChannels; channel++)
                    buffer[offset + frame * inputChannels + channel] = _buffer[frame * inputChannels + channel] * gain;
            }
            return frames * channels;
        }
        protected override void SeekCore(int frame) => reader.Seek(frame);
        protected override void DisposeCore() => reader.Dispose();
    }
    private sealed class CallbackReader(int frameCount, int channelCount, int sampleRate,
        PCMReaderCallback callback, PCMSetPositionCallback? seek) : AudioSampleReader
    {
        private float[] _buffer = [];
        private int _position;
        public override int channels => channelCount;
        public override int frequency => sampleRate;
        public override int samples => frameCount;
        public override int position => _position;
        protected override int ReadCore(float[] buffer, int offset, int count)
        {
            var frames = Math.Min(Math.Min(4096, count / channels), samples - _position);
            if (frames == 0) return 0;
            var length = frames * channels;
            if (_buffer.Length != length) _buffer = new float[length]; else Array.Clear(_buffer);
            seek?.Invoke(_position); callback(_buffer); _buffer.CopyTo(buffer, offset); _position += frames; return length;
        }
        protected override void SeekCore(int frame)
        {
            if (seek is null && frame != _position) throw new NotSupportedException("This procedural clip requires a position callback for seeking/looping.");
            seek?.Invoke(frame); _position = frame;
        }
    }
}
