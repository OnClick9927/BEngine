using System.Buffers.Binary;

namespace BEngine.Audio;

internal sealed class WaveSampleReader : AudioSampleReader
{
    private readonly Stream _stream;
    private readonly long _dataOffset;
    private readonly int _bits, _format, _stride;
    private int _position;
    private readonly byte[] _buffer;
    public override int channels { get; }
    public override int frequency { get; }
    public override int samples { get; }
    public override int position => _position;

    internal WaveSampleReader(Stream stream)
    {
        _stream = stream; Span<byte> header = stackalloc byte[12]; stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8)) throw new InvalidDataException("Invalid WAVE header.");
        var end = checked(stream.Position - 4 + BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]));
        if (end > stream.Length) throw new InvalidDataException("Truncated WAVE container.");
        long dataSize = 0; int channelsValue = 0, rate = 0, bits = 0, format = 0, stride = 0;
        Span<byte> chunk = stackalloc byte[8]; Span<byte> fmt = stackalloc byte[40];
        using var budget = LoopExecutionGuard.AllowIterations((int)Math.Min(int.MaxValue, stream.Length / 8 + 1));
        while (stream.Position <= end - 8)
        {
            stream.ReadExactly(chunk); var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]); var start = stream.Position;
            if (size > end - start) throw new InvalidDataException("WAVE chunk exceeds its container.");
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16) throw new InvalidDataException("Incomplete WAVE format.");
                stream.ReadExactly(fmt[..(int)Math.Min(40, size)]);
                format = BinaryPrimitives.ReadUInt16LittleEndian(fmt); channelsValue = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]); stride = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]); bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (format == 0xfffe)
                {
                    if (size < 40 || BinaryPrimitives.ReadUInt16LittleEndian(fmt[16..]) < 22 ||
                        !fmt[28..40].SequenceEqual(new byte[] {0,0,16,0,128,0,0,170,0,56,155,113})) throw new InvalidDataException("Unsupported extensible WAVE format.");
                    format = BinaryPrimitives.ReadInt32LittleEndian(fmt[24..]);
                }
            }
            else if (chunk[..4].SequenceEqual("data"u8) && dataSize == 0) { _dataOffset = start; dataSize = size; }
            stream.Position = start + size + (size & 1);
        }
        if (channelsValue is < 1 or > 8 || rate is < 8000 or > 384000 || format is not (1 or 3) ||
            format == 1 && bits is not (8 or 16 or 24 or 32) || format == 3 && bits != 32 || stride != channelsValue * (bits / 8) || dataSize == 0 || dataSize % stride != 0)
            throw new InvalidDataException("Unsupported or incomplete PCM/float WAVE format.");
        channels = channelsValue; frequency = rate; samples = checked((int)(dataSize / stride));
        _bits = bits; _format = format; _stride = stride; _buffer = new byte[4096 * stride]; stream.Position = _dataOffset;
    }
    protected override int ReadCore(float[] buffer, int offset, int count)
    {
        var frames = Math.Min(Math.Min(4096, count / channels), samples - _position); var bytes = frames * _stride;
        _stream.ReadExactly(_buffer.AsSpan(0, bytes)); var width = _bits / 8;
        using var budget = LoopExecutionGuard.AllowIterations(frames * channels + 1);
        for (var i = 0; i < frames * channels; i++)
        {
            var input = _buffer.AsSpan(i * width, width);
            float value;
            if (_format == 3) value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(input));
            else value = _bits switch
            {
                8 => (input[0] - 128) / 128f,
                16 => BinaryPrimitives.ReadInt16LittleEndian(input) / 32768f,
                24 => ((input[0] | input[1] << 8 | input[2] << 16) << 8 >> 8) / 8388608f,
                32 => BinaryPrimitives.ReadInt32LittleEndian(input) / 2147483648f,
                _ => 0
            };
            buffer[offset + i] = value;
        }
        _position += frames; return frames * channels;
    }
    protected override void SeekCore(int frame) { _stream.Position = _dataOffset + (long)frame * _stride; _position = frame; }
    protected override void DisposeCore() => _stream.Dispose();
}
