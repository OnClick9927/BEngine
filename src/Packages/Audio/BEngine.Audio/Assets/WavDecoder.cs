using System.Buffers.Binary;

namespace BEngine.Audio;

internal static class WavDecoder
{
    internal static DecodedAudio Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 44 || !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Audio data is not a RIFF/WAVE file.");

        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        ReadOnlySpan<byte> sampleBytes = default;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var id = bytes.Slice(offset, 4);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4)));
            var start = offset + 8;
            if (size < 0 || start + size > bytes.Length)
                throw new InvalidDataException("WAVE chunk exceeds the source data.");
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16) throw new InvalidDataException("WAVE fmt chunk is incomplete.");
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(start, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(start + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(start + 4, 4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(start + 12, 2));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(start + 14, 2));
            }
            else if (id.SequenceEqual("data"u8)) sampleBytes = bytes.Slice(start, size);
            offset = checked(start + size + (size & 1));
        }

        if (format is not (1 or 3) || channels is < 1 or > 8 || sampleRate is < 8000 or > 384000 ||
            blockAlign == 0 || sampleBytes.IsEmpty)
            throw new InvalidDataException("WAVE format is unsupported or incomplete.");
        var bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample == 0 || blockAlign != channels * bytesPerSample ||
            format == 1 && bitsPerSample is not (8 or 16 or 24 or 32) ||
            format == 3 && bitsPerSample != 32)
            throw new InvalidDataException($"Unsupported WAVE encoding: format {format}, {bitsPerSample}-bit.");

        var frameCount = sampleBytes.Length / blockAlign;
        var samples = new float[checked(frameCount * channels)];
        for (var index = 0; index < samples.Length; index++)
        {
            var source = sampleBytes.Slice(index * bytesPerSample, bytesPerSample);
            samples[index] = format == 3
                ? Math.Clamp(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source)), -1f, 1f)
                : bitsPerSample switch
                {
                    8 => (source[0] - 128) / 128f,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(source) / 32768f,
                    24 => ReadInt24(source) / 8388608f,
                    32 => BinaryPrimitives.ReadInt32LittleEndian(source) / 2147483648f,
                    _ => 0
                };
        }
        return new DecodedAudio(samples, channels, sampleRate, frameCount);
    }

    private static int ReadInt24(ReadOnlySpan<byte> bytes)
    {
        var value = bytes[0] | bytes[1] << 8 | bytes[2] << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xff000000) : value;
    }

    internal readonly record struct DecodedAudio(float[] Samples, int Channels, int SampleRate, int FrameCount);
}
