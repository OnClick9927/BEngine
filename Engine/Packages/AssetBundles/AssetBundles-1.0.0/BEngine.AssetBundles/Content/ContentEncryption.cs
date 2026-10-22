using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BEngine.Content;

/// <summary>Seekable AES-256-GCM content containers. Decrypted content is never written to disk.</summary>
public static class ContentEncryption
{
    private const int HeaderSize = 128;
    private const int ChunkSize = 64 * 1024;
    private const int TagSize = 16;
    private static ReadOnlySpan<byte> Magic => "BENGENC1"u8;

    /// <summary>Encrypts a seekable input. Same content and key produce identical bytes for immutable releases.
    /// Embedded keys allow standalone Player bootstrap; they are reversible client-side key storage.</summary>
    public static void Write(Stream input, Stream output, ContentEncryptionKey key,
        bool embedKey = false, Action? checkCancellation = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(key);
        checkCancellation?.Invoke();
        if (!input.CanSeek || !input.CanRead || !output.CanWrite || ReferenceEquals(input, output))
            throw new ArgumentException("Encryption requires a seekable input and a separate writable output.");
        var origin = input.Position;
        var length = input.Length - origin;
        var chunks = length / ChunkSize + (length % ChunkSize == 0 ? 0 : 1);
        if (length < 0 || chunks >= uint.MaxValue) throw new InvalidDataException("Encrypted content is too large.");
        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)(embedKey ? 1 : 0));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), ChunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), length);
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[ChunkSize];
            try
            {
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    checkCancellation?.Invoke();
                    hash.AppendData(buffer.AsSpan(0, read));
                }
                hash.GetHashAndReset().CopyTo(header, 24);
            }
            finally { CryptographicOperations.ZeroMemory(buffer); }
        }
        input.Position = origin;
        Convert.FromHexString(key.Fingerprint).CopyTo(header, 56);
        if (embedKey)
        {
            var mask = CreateEmbeddedMask(header.AsSpan(24, 32));
            for (var i = 0; i < 32; i++) header[72 + i] = (byte)(key.Material[i] ^ mask[i]);
        }
        var fileKey = DeriveFileKey(key, header.AsSpan(0, 112));
        var plaintext = new byte[ChunkSize];
        try
        {
            using var aes = new AesGcm(fileKey, TagSize);
            Span<byte> nonce = stackalloc byte[12];
            // Distinct content or header metadata gets a distinct subkey, preventing nonce
            // reuse across different plaintext/AAD while retaining reproducible releases.
            SetNonce(nonce, uint.MaxValue);
            aes.Encrypt(nonce, [], [], header.AsSpan(112, TagSize), header.AsSpan(0, 112));
            output.Write(header);
            var ciphertext = new byte[ChunkSize];
            var tag = new byte[TagSize];
            using var verifiedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (uint chunk = 0; chunk < chunks; chunk++)
            {
                checkCancellation?.Invoke();
                var count = (int)Math.Min(ChunkSize, length - (long)chunk * ChunkSize);
                input.ReadExactly(plaintext.AsSpan(0, count));
                verifiedHash.AppendData(plaintext.AsSpan(0, count));
                SetNonce(nonce, chunk);
                aes.Encrypt(nonce, plaintext.AsSpan(0, count), ciphertext.AsSpan(0, count), tag, header);
                output.Write(ciphertext.AsSpan(0, count));
                output.Write(tag);
            }
            if (input.Position != input.Length ||
                !CryptographicOperations.FixedTimeEquals(verifiedHash.GetHashAndReset(), header.AsSpan(24, 32)))
                throw new InvalidDataException("Content changed while it was being encrypted.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    /// <summary>Takes ownership of input. Legacy plaintext streams are returned unchanged.
    /// Encrypted streams authenticate the header and each accessed chunk.</summary>
    public static Stream OpenRead(Stream input, ContentEncryptionKey? key = null, bool requireEncryption = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            if (!input.CanRead || !input.CanSeek || input.Position != 0)
                throw new ArgumentException("Content input must be readable, seekable and positioned at zero.");
            Span<byte> magic = stackalloc byte[8];
            var read = input.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
            input.Position = 0;
            if (read != magic.Length || !magic.SequenceEqual(Magic))
            {
                if (requireEncryption) throw new InvalidDataException("Encrypted content was required.");
                return input;
            }
            var header = new byte[HeaderSize];
            input.ReadExactly(header);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
            var length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16));
            var chunks = length < 0 ? -1 : length / ChunkSize + (length % ChunkSize == 0 ? 0 : 1);
            if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) != 1 || flags > 1 ||
                BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12)) != ChunkSize ||
                length < 0 || chunks >= uint.MaxValue || header.AsSpan(104, 8).IndexOfAnyExcept((byte)0) >= 0 ||
                input.Length != checked(HeaderSize + length + chunks * TagSize))
                throw new InvalidDataException("Encrypted content header is invalid.");
            if (key is null && flags == 1)
            {
                var material = new byte[32];
                var mask = CreateEmbeddedMask(header.AsSpan(24, 32));
                for (var i = 0; i < 32; i++) material[i] = (byte)(header[72 + i] ^ mask[i]);
                try { key = new ContentEncryptionKey(material); }
                finally { CryptographicOperations.ZeroMemory(material); }
            }
            if (key is null) throw new InvalidDataException("An Encryption Key is required to read this content.");
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(key.Fingerprint), header.AsSpan(56, 16)))
                throw new InvalidDataException("Encryption Key does not match this content.");
            return new DecryptingStream(input, header, key, length);
        }
        catch (CryptographicException exception)
        {
            input.Dispose();
            throw new InvalidDataException("Encrypted content authentication failed.", exception);
        }
        catch { input.Dispose(); throw; }
    }

    internal static ContentEncryptionKey? GetKey(Stream stream) =>
        (stream as DecryptingStream)?.Key;

    private static byte[] DeriveFileKey(ContentEncryptionKey key, ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> domain = "BEngine.ContentEncryption/v1/file"u8;
        var input = new byte[domain.Length + header.Length];
        domain.CopyTo(input);
        header.CopyTo(input.AsSpan(domain.Length));
        return HMACSHA256.HashData(key.Material, input);
    }

    private static byte[] CreateEmbeddedMask(ReadOnlySpan<byte> contentHash)
    {
        ReadOnlySpan<byte> domain = "BEngine.ContentEncryption/v1/client-key"u8;
        var input = new byte[domain.Length + contentHash.Length];
        domain.CopyTo(input);
        contentHash.CopyTo(input.AsSpan(domain.Length));
        return SHA256.HashData(input);
    }

    private static void SetNonce(Span<byte> nonce, uint chunk)
    {
        nonce.Clear();
        "BENGENC1"u8.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce[8..], chunk);
    }

    private sealed class DecryptingStream : Stream
    {
        private readonly Stream _source;
        private readonly byte[] _header;
        private readonly AesGcm _aes;
        private readonly byte[] _plain = new byte[ChunkSize];
        private readonly byte[] _cipher = new byte[ChunkSize + TagSize];
        private readonly long _length;
        private long _position;
        private long _cachedChunk = -1;
        private bool _disposed;

        internal DecryptingStream(Stream source, byte[] header, ContentEncryptionKey key, long length)
        {
            _source = source; _header = header; Key = key; _length = length;
            var fileKey = DeriveFileKey(key, header.AsSpan(0, 112));
            try { _aes = new AesGcm(fileKey, TagSize); }
            finally { CryptographicOperations.ZeroMemory(fileKey); }
            Span<byte> nonce = stackalloc byte[12];
            SetNonce(nonce, uint.MaxValue);
            try { _aes.Decrypt(nonce, [], header.AsSpan(112, TagSize), [], header.AsSpan(0, 112)); }
            catch { _aes.Dispose(); throw; }
        }

        internal ContentEncryptionKey Key { get; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length { get { ThrowIfDisposed(); return _length; } }
        public override long Position
        {
            get { ThrowIfDisposed(); return _position; }
            set { ThrowIfDisposed(); if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _position = value; }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }
        public override int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();
            var total = 0;
            Span<byte> nonce = stackalloc byte[12];
            while (buffer.Length > 0 && _position < _length)
            {
                var chunk = _position / ChunkSize;
                var count = (int)Math.Min(ChunkSize, _length - chunk * ChunkSize);
                if (chunk != _cachedChunk)
                {
                    _cachedChunk = -1;
                    _source.Position = HeaderSize + chunk * (ChunkSize + TagSize);
                    _source.ReadExactly(_cipher.AsSpan(0, count + TagSize));
                    SetNonce(nonce, checked((uint)chunk));
                    try { _aes.Decrypt(nonce, _cipher.AsSpan(0, count), _cipher.AsSpan(count, TagSize), _plain.AsSpan(0, count), _header); }
                    catch (CryptographicException exception)
                    {
                        CryptographicOperations.ZeroMemory(_plain);
                        throw new InvalidDataException("Encrypted content chunk authentication failed.", exception);
                    }
                    _cachedChunk = chunk;
                }
                var offset = (int)(_position % ChunkSize);
                var copied = Math.Min(buffer.Length, count - offset);
                _plain.AsSpan(offset, copied).CopyTo(buffer);
                buffer = buffer[copied..]; _position += copied; total += copied;
            }
            return total;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = checked(origin switch
            {
                SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin))
            });
            return _position;
        }
        public override void Flush() => ThrowIfDisposed();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true; _aes.Dispose(); CryptographicOperations.ZeroMemory(_plain); _source.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
