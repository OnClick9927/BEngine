using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BEngine.Rendering;

/// <summary>Validates a BTEX stream, then reads only resident mip payloads without retaining the full image.</summary>
public sealed class TextureMipStream
{
    public int Width { get; } public int Height { get; } public TextureCompressionFormat Format { get; }
    public int MipCount => _levels.Length;
    private readonly (long Offset, int Size)[] _levels;
    public TextureMipStream(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek || stream.Length < 60 || stream.Length > ImageCodec.MaximumDecodedBytes * 2L) throw new InvalidDataException("Invalid seekable texture stream.");
        stream.Position = 0; Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
        if (!TextureImageData.IsContainer(header)) throw new InvalidDataException("Invalid texture container signature.");
        Width = BinaryPrimitives.ReadInt32LittleEndian(header[8..]); Height = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        Format = (TextureCompressionFormat)BinaryPrimitives.ReadInt32LittleEndian(header[16..]); var count = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
        if (!ImageCodec.ValidSize(Width, Height) || count < 1 || count > 1 + (int)Math.Log2(Math.Max(Width, Height))) throw new InvalidDataException("Invalid mip dimensions/count.");
        _levels = new (long, int)[count]; Span<byte> lengthBytes = stackalloc byte[4];
        for (var level = 0; level < count; level++)
        {
            stream.ReadExactly(lengthBytes); var size = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (size != TextureImageData.LevelSize(Math.Max(1, Width >> level), Math.Max(1, Height >> level), Format) || size > stream.Length - 32 - stream.Position)
                throw new InvalidDataException("Invalid mip payload size.");
            _levels[level] = (stream.Position, size); stream.Seek(size, SeekOrigin.Current);
        }
        if (stream.Position != stream.Length - 32) throw new InvalidDataException("Unexpected mip trailer.");
        var expected = new byte[32]; stream.ReadExactly(expected); stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[64 * 1024]; var remaining = stream.Length - 32;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested(); var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer.AsSpan(0, read)); remaining -= read;
        }
        if (!hash.GetHashAndReset().AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Texture stream checksum mismatch.");
    }
    public TextureImageData ReadLevels(Stream stream, int firstLevel, CancellationToken cancellationToken = default)
    {
        if ((uint)firstLevel >= _levels.Length) throw new ArgumentOutOfRangeException(nameof(firstLevel));
        var bytes = new List<byte[]>();
        for (var level = firstLevel; level < _levels.Length; level++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var data = new byte[_levels[level].Size];
            stream.Position = _levels[level].Offset; stream.ReadExactly(data); bytes.Add(data);
        }
        return new(Math.Max(1, Width >> firstLevel), Math.Max(1, Height >> firstLevel), Format, bytes);
    }
    public async BTask<TextureImageData> ReadLevelsAsync(Stream stream, int firstLevel, CancellationToken cancellationToken = default)
    {
        if ((uint)firstLevel >= _levels.Length) throw new ArgumentOutOfRangeException(nameof(firstLevel));
        if (BackgroundWorkScheduler.IsSupported)
        {
            using var bridge = new BCancellationSource();
            using var registration = cancellationToken.Register(static state => ((BCancellationSource)state!).Cancel(), bridge);
            return await BackgroundWorkScheduler.Main.Run(token => ReadLevelsOnWorker(stream, firstLevel, token),
                bridge.Token);
        }
        var bytes = new List<byte[]>();
        for (var level = firstLevel; level < _levels.Length; level++)
        {
            var data = new byte[_levels[level].Size]; stream.Position = _levels[level].Offset;
            // Bound synchronous work per frame, including on hosts whose stream cannot read asynchronously.
            for (var offset = 0; offset < data.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.ReadExactly(data.AsSpan(offset, Math.Min(64 * 1024, data.Length - offset)));
                offset += Math.Min(64 * 1024, data.Length - offset);
                await BTask.Yield();
            }
            bytes.Add(data);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(Math.Max(1, Width >> firstLevel), Math.Max(1, Height >> firstLevel), Format, bytes);
    }

    private TextureImageData ReadLevelsOnWorker(Stream stream, int firstLevel, CancellationToken cancellationToken)
    {
        var bytes = new List<byte[]>(_levels.Length - firstLevel);
        for (var level = firstLevel; level < _levels.Length; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = new byte[_levels[level].Size];
            stream.Position = _levels[level].Offset;
            stream.ReadExactly(data);
            bytes.Add(data);
        }
        return new(Math.Max(1, Width >> firstLevel), Math.Max(1, Height >> firstLevel), Format, bytes);
    }
}
