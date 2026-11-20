using System.Security.Cryptography;

namespace BEngine.Platform.DotNet;

public sealed class SystemSha256Service : IPlatformSha256Service
{
    public static SystemSha256Service Shared { get; } = new();
    public IPlatformIncrementalSha256 CreateIncremental() => new Digest();
    public bool CanComputeAsync(long byteCount) => false;
    public IPlatformSha256Operation? TryBegin(ReadOnlySpan<byte> input) =>
        throw new PlatformNotSupportedException("This host uses its incremental SHA-256 service.");

    private sealed class Digest : IPlatformIncrementalSha256
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _disposed, _finalized;
        public void Append(ReadOnlySpan<byte> input) { Available(); _hash.AppendData(input); }
        public void FinalizeHash(Span<byte> destination)
        {
            Available();
            if (destination.Length < 32) throw new ArgumentException("The digest requires at least 32 bytes.", nameof(destination));
            if (!_hash.TryGetHashAndReset(destination, out var written) || written != 32) throw new CryptographicException("SHA-256 did not produce a complete digest.");
            _finalized = true;
        }
        public void Reset()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> discard = stackalloc byte[32];
            _hash.TryGetHashAndReset(discard, out _); _finalized = false;
        }
        private void Available()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_finalized) throw new InvalidOperationException("The SHA-256 state has already been finalized; reset it before reuse.");
        }
        public void Dispose() { if (_disposed) return; _disposed = true; _hash.Dispose(); }
    }
}
