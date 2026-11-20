using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BEngine.Tooling;

internal static class PackageCatalogTransport
{
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    internal static string PipeName(string root)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        var identity = Environment.UserDomainName + "\\" + Environment.UserName + "\n" + normalized;
        return "BEngine.Packages." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    }

    internal static async Task WriteAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length > MaximumMessageBytes) throw new InvalidDataException("Package catalog message is too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumMessageBytes) throw new InvalidDataException("Invalid package catalog message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
