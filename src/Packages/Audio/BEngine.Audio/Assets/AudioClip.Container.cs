using System.Buffers.Binary;

namespace BEngine.Audio;

public sealed partial class AudioClip
{
    private static ReadOnlySpan<byte> ContainerMagic => "BGAUDIO2"u8;
    private static bool IsContainer(ReadOnlySpan<byte> bytes) => bytes.StartsWith(ContainerMagic) || bytes.StartsWith("BGAUDIO1"u8);
    private const int ContainerHeaderSize = 18;
    private void ReadContainer(ReadOnlyMemory<byte> bytes)
    {
        if (IsContainer(bytes.Span))
        {
            ReadContainerHeader(bytes.Span, bytes.Length);
            _encoded = bytes[ContainerHeaderSize..].ToArray();
        }
        else _encoded = bytes.ToArray();
    }
    private void ReadContainerHeader(ReadOnlySpan<byte> data, long length)
    {
        var legacy = data.StartsWith("BGAUDIO1"u8);
        if (data.Length < ContainerHeaderSize || data[8] > 1 || data[9] > (legacy ? 1 : 7) ||
            BinaryPrimitives.ReadInt32LittleEndian(data[14..18]) != length - ContainerHeaderSize)
            throw new InvalidDataException("Invalid imported audio container.");
        loadType = (AudioClipLoadType)data[8]; _mono = (data[9] & 1) != 0; _gain = BinaryPrimitives.ReadSingleLittleEndian(data[10..14]);
        loadInBackground = (data[9] & 2) != 0; preloadAudioData = (data[9] & 4) == 0;
        if (!float.IsFinite(_gain) || _gain < 0 || _gain > 100_000_000) throw new InvalidDataException("Invalid audio normalization gain.");
    }
    internal static byte[] BuildImportedBytes(byte[] source, AudioClipLoadType type, bool forceMono, bool normalize,
        bool background = false, bool preload = true, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        var gain = 1f;
        using (var decoded = AudioDecoder.Open(new MemoryStream(source, writable: false)))
        {
            ValidateFormat(decoded.samples, decoded.channels, decoded.frequency);
            if (normalize)
            {
                var buffer = new float[4096 * decoded.channels]; var peak = 0f;
                using var budget = LoopExecutionGuard.AllowIterations((int)Math.Min(int.MaxValue, (long)decoded.samples * decoded.channels + 1));
                int read;
                while ((read = decoded.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var frame = 0; frame < read / decoded.channels; frame++)
                    {
                        float sum = 0;
                        for (var channel = 0; channel < decoded.channels; channel++)
                        { var value = buffer[frame * decoded.channels + channel]; if (!forceMono) peak = Math.Max(peak, Math.Abs(value)); sum += value; }
                        if (forceMono) peak = Math.Max(peak, Math.Abs(sum / decoded.channels));
                    }
                }
                if (peak > 1e-8f && peak < 1) gain = 1 / peak;
            }
        }
        var result = new byte[checked(ContainerHeaderSize + source.Length)];
        ContainerMagic.CopyTo(result); result[8] = (byte)type; result[9] = (byte)((forceMono ? 1 : 0) | (background ? 2 : 0) | (preload ? 0 : 4));
        BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(10), gain); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), source.Length);
        source.CopyTo(result, ContainerHeaderSize); return result;
    }
}
