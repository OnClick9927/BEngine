namespace BEngine.Audio;

internal static class AudioDecoder
{
    // Ownership of the stream always transfers, including failed decoder initialization.
    internal static AudioSampleReader Open(Stream stream)
    {
        try
        {
            if (!stream.CanRead || !stream.CanSeek) throw new InvalidDataException("Audio sources must be readable and seekable.");
            var start = stream.Position; Span<byte> header = stackalloc byte[12];
            var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false); stream.Position = start;
            AudioSampleReader reader;
            if (length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8)) reader = new WaveSampleReader(stream);
            else if (length >= 4 && header[..4].SequenceEqual("OggS"u8)) reader = new VorbisSampleReader(stream);
            else if (length >= 3 && (header[..3].SequenceEqual("ID3"u8) || header[0] == 0xff && (header[1] & 0xe0) == 0xe0)) reader = new MpegSampleReader(stream);
            else throw new InvalidDataException("Unsupported audio data. Expected PCM/float WAVE, Ogg Vorbis or MPEG audio.");
            try
            {
                if (reader.channels is < 1 or > 8 || reader.frequency is < 8000 or > 384000 || reader.samples <= 0)
                    throw new InvalidDataException("Invalid audio channel count, frequency or duration.");
                return reader;
            }
            catch { reader.Dispose(); throw; }
        }
        catch { stream.Dispose(); throw; }
    }

    private sealed class VorbisSampleReader : AudioSampleReader
    {
        private NVorbis.VorbisReader _reader;
        private readonly Stream _stream;
        private readonly float[] _skip;
        private int _position;
        internal VorbisSampleReader(Stream stream)
        { _stream = stream; _reader = new(stream, closeOnDispose: false); _skip = new float[4096 * _reader.Channels]; }
        public override int channels => _reader.Channels;
        public override int frequency => _reader.SampleRate;
        public override int samples => checked((int)_reader.TotalSamples);
        public override int position => _position;
        protected override int ReadCore(float[] buffer, int offset, int count)
        { var read = _reader.ReadSamples(buffer, offset, (int)Math.Min(count, (long)(samples - _position) * channels)); _position += read / channels; return read; }
        protected override void SeekCore(int frame)
        {
            // NVorbis 0.10.5 page seeks do not reproduce sequential PCM for all files.
            // Keep exact sample positions and overlap state with bounded decode buffers.
            if (frame < _position) { _reader.Dispose(); _stream.Position = 0; _reader = new(_stream, false); _position = 0; }
            using var budget = LoopExecutionGuard.AllowIterations((frame - _position) / 4096 + 2);
            while (_position < frame)
                if (ReadCore(_skip, 0, Math.Min(_skip.Length / channels, frame - _position) * channels) == 0)
                    throw new InvalidDataException("Vorbis stream ended before the requested frame.");
        }
        protected override void DisposeCore() { _reader.Dispose(); _stream.Dispose(); }
    }

    private sealed class MpegSampleReader : AudioSampleReader
    {
        private readonly Stream _stream;
        private NLayer.MpegFile _reader;
        private int _position;
        private readonly int _frames;
        private readonly float[] _skip;
        private readonly int _initialSkip;
        internal MpegSampleReader(Stream stream)
        {
            _stream = IO.MpegGaplessReadStream.Prepare(stream, out _initialSkip, out var gaplessFrames); _reader = NLayerCompatibility.Open(_stream);
            try
            {
                _frames = gaplessFrames > 0 ? gaplessFrames : checked((int)(_reader.Length / sizeof(float) / _reader.Channels));
                _skip = new float[4096 * _reader.Channels]; SkipDelay();
            }
            catch { _reader.Dispose(); throw; }
        }
        public override int channels => _reader.Channels;
        public override int frequency => _reader.SampleRate;
        public override int samples => _frames;
        public override int position => _position;
        protected override int ReadCore(float[] buffer, int offset, int count)
        {
            var read = _reader.ReadSamples(buffer, offset, (int)Math.Min(count, (long)(samples - _position) * channels));
            _position += read / channels; return read;
        }
        protected override void SeekCore(int frame)
        {
            // Sequential decoding preserves encoder-delay trimming and the bit reservoir.
            // NLayer's byte-position setter is frame-aligned, not an exact PCM frame seek.
            if (frame < _position) { _reader.Dispose(); _stream.Position = 0; _reader = NLayerCompatibility.Open(_stream); _position = 0; SkipDelay(); }
            using var budget = LoopExecutionGuard.AllowIterations((frame - _position) / 4096 + 2);
            while (_position < frame)
                if (ReadCore(_skip, 0, Math.Min(_skip.Length / channels, frame - _position) * channels) == 0)
                    throw new InvalidDataException("MPEG stream ended before the requested frame.");
        }
        private void SkipDelay()
        {
            var remaining = _initialSkip * channels;
            while (remaining > 0)
            {
                var read = _reader.ReadSamples(_skip, 0, Math.Min(_skip.Length, remaining));
                if (read == 0) throw new InvalidDataException("Truncated MPEG encoder delay.");
                remaining -= read;
            }
        }
        protected override void DisposeCore() { _reader.Dispose(); _stream.Dispose(); }
    }
}
