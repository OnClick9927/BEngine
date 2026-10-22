using System.Security.Cryptography;
using System.Text;

namespace BEngine.Content;

/// <summary>A content key derived from a build-time passphrase. Never logs the key material.</summary>
public sealed class ContentEncryptionKey
{
    private readonly byte[] _material;

    internal ContentEncryptionKey(ReadOnlySpan<byte> material)
    {
        if (material.Length != 32) throw new ArgumentException("Content keys require 32 bytes.");
        _material = material.ToArray();
        Fingerprint = Convert.ToHexString(SHA256.HashData(_material).AsSpan(0, 16)).ToLowerInvariant();
    }

    public string Fingerprint { get; }
    internal ReadOnlySpan<byte> Material => _material;

    public static ContentEncryptionKey FromPassphrase(string passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        var bytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            if (bytes.Length is < 16 or > 1024)
                throw new ArgumentException("Encryption Key must contain 16-1024 UTF-8 bytes.", nameof(passphrase));
            var derived = Rfc2898DeriveBytes.Pbkdf2(bytes,
                "BEngine.ContentEncryption/v1/passphrase"u8, 100_000, HashAlgorithmName.SHA256, 32);
            try { return new ContentEncryptionKey(derived); }
            finally { CryptographicOperations.ZeroMemory(derived); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string GeneratePassphrase() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public override string ToString() => $"ContentEncryptionKey:{Fingerprint}";
}
