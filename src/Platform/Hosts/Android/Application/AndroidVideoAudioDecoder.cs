using System;
using System.Collections.Generic;
using System.IO;
using Android.Media;
using BEngine.Platform;

namespace BEngine.Player.Hosts.Android;

internal sealed class AndroidVideoAudioDecoder : TimestampedPcmReader
{
    private readonly MediaExtractor _extractor = new();
    private MediaCodec? _codec;
    private readonly MediaCodec.BufferInfo _info = new();
    private string? _temporary;
    private bool _inputEnded, _outputEnded;
    private int _encoding = 2;
    public override int Channels { get; }
    public override int Frequency { get; }
    public override int Samples { get; }
    internal AndroidVideoAudioDecoder(string source, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (!bytes.IsEmpty) { _temporary = Path.Combine(Path.GetTempPath(), "bengine-audio-" + Guid.NewGuid().ToString("N")); File.WriteAllBytes(_temporary, bytes.ToArray()); source = _temporary; }
            _extractor.SetDataSource(source, new Dictionary<string, string>());
            MediaFormat? selected = null;
            for (var index = 0; index < _extractor.TrackCount; index++)
            {
                var format = _extractor.GetTrackFormat(index);
                if (format.GetString(MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true)
                { _extractor.SelectTrack(index); selected = format; break; }
                format.Dispose();
            }
            using var audio = selected ?? throw new InvalidDataException("No Android container audio track.");
            Channels = audio.GetInteger(MediaFormat.KeyChannelCount); Frequency = audio.GetInteger(MediaFormat.KeySampleRate);
            var duration = audio.GetLong(MediaFormat.KeyDuration);
            if (Channels is < 1 or > 8 || Frequency is < 8000 or > 192000 || duration <= 0 || duration * (double)Frequency / 1_000_000 > int.MaxValue)
                throw new InvalidDataException("Android audio metadata exceeds mixer limits.");
            Samples = checked((int)Math.Ceiling(duration * (double)Frequency / 1_000_000));
            _codec = MediaCodec.CreateDecoderByType(audio.GetString(MediaFormat.KeyMime)!);
            _codec.Configure(audio, null, null, 0); _codec.Start();
        }
        catch { Dispose(); throw; }
    }
    protected override bool ReadPacket(out int startFrame, out float[] samples)
    {
        startFrame = 0; samples = [];
        var codec = _codec ?? throw new ObjectDisposedException(nameof(AndroidVideoAudioDecoder));
        var deadline = Environment.TickCount64 + 10000;
        while (!_outputEnded)
        {
            if (Environment.TickCount64 > deadline) throw new IOException("Android audio decoder timed out.");
            if (!_inputEnded)
            {
                var inputIndex = codec.DequeueInputBuffer(1000);
                if (inputIndex >= 0)
                {
                    using var input = codec.GetInputBuffer(inputIndex) ?? throw new InvalidDataException("No Android audio input buffer.");
                    var size = _extractor.ReadSampleData(input, 0);
                    if (size < 0) { codec.QueueInputBuffer(inputIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); _inputEnded = true; }
                    else { codec.QueueInputBuffer(inputIndex, 0, size, _extractor.SampleTime, 0); _extractor.Advance(); }
                }
            }
            var outputIndex = codec.DequeueOutputBuffer(_info, 1000);
            if (outputIndex == (int)MediaCodecInfoState.OutputFormatChanged)
            {
                using var format = codec.OutputFormat;
                if (format.GetInteger(MediaFormat.KeyChannelCount) != Channels || format.GetInteger(MediaFormat.KeySampleRate) != Frequency)
                    throw new InvalidDataException("Android audio format changed during decoding.");
                if (format.ContainsKey(MediaFormat.KeyPcmEncoding)) _encoding = format.GetInteger(MediaFormat.KeyPcmEncoding);
                if (_encoding is not (2 or 4)) throw new NotSupportedException("Android audio decoder must return PCM16 or float PCM.");
                continue;
            }
            if (outputIndex < 0) continue;
            try
            {
                _outputEnded = (_info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                if (_info.Size == 0) continue;
                var stride = _encoding == 4 ? 4 : 2;
                if (_info.Size > 16 * 1024 * 1024 || _info.Size % (stride * Channels) != 0) throw new InvalidDataException("Android PCM buffer is invalid.");
                var data = new byte[_info.Size];
                using var output = codec.GetOutputBuffer(outputIndex) ?? throw new InvalidDataException("No Android audio output buffer.");
                output.Position(_info.Offset); output.Get(data);
                samples = new float[data.Length / stride];
                for (var index = 0; index < samples.Length; index++) samples[index] = stride == 4 ? BitConverter.ToSingle(data, index * 4) : BitConverter.ToInt16(data, index * 2) / 32768f;
                startFrame = checked((int)Math.Round(_info.PresentationTimeUs * (Frequency / 1_000_000d))); return true;
            }
            finally { codec.ReleaseOutputBuffer(outputIndex, false); }
        }
        return false;
    }
    protected override void SeekDecoder(int frame)
    {
        _extractor.SeekTo((long)(Math.Max(0, frame / (double)Frequency - .25) * 1_000_000), MediaExtractorSeekTo.ClosestSync);
        _codec!.Flush(); _inputEnded = _outputEnded = false;
    }
    protected override void DisposeDecoder()
    {
        _codec?.Dispose(); _codec = null; _info.Dispose(); _extractor.Dispose();
        if (_temporary is not null) { File.Delete(_temporary); _temporary = null; }
    }
}
