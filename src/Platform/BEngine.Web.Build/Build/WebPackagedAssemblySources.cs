using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace BEngine.Editor;

/// <summary>Finds exact frozen PE images already shipped in bounded AssetBundle archives.</summary>
internal static class WebPackagedAssemblySources
{
    internal const int MaximumArchiveBytes = 16 * 1024 * 1024;
    internal const int MaximumAcceptedArchiveBytes = 32 * 1024 * 1024;
    private const int MaximumImageBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>Unverifiable inputs remain inline; only existing StreamingAssets files are inspected.</summary>
    internal static IReadOnlyDictionary<string, WebPackagedAssemblySource> Discover(
        string webRoot, IReadOnlyCollection<string> frozenAssemblyPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webRoot);
        ArgumentNullException.ThrowIfNull(frozenAssemblyPaths);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, WebPackagedAssemblySource>(comparer);
        var expected = new Dictionary<string, List<FrozenImage>>(StringComparer.Ordinal);
        foreach (var path in frozenAssemblyPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = ReadFrozenImage(path);
            if (image is null) continue;
            if (!expected.TryGetValue(image.Sha256, out var aliases)) expected.Add(image.Sha256, aliases = []);
            if (!aliases.Any(alias => comparer.Equals(alias.Path, image.Path))) aliases.Add(image);
        }
        if (expected.Count == 0) return result;

        var root = Path.GetFullPath(webRoot);
        var streamingRoot = Path.Combine(root, "StreamingAssets");
        long acceptedBytes = 0;
        foreach (var archivePath in EnumerateArchives(streamingRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var relative = Path.GetRelativePath(root, Path.GetFullPath(archivePath)).Replace('\\', '/');
                if (!IsSafeRelativePath(relative) || !relative.StartsWith("StreamingAssets/", StringComparison.Ordinal)) continue;
                var info = new FileInfo(archivePath);
                if (info.Length is <= 0 or > MaximumArchiveBytes || acceptedBytes + info.Length > MaximumAcceptedArchiveBytes ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var bytes = File.ReadAllBytes(archivePath);
                if (bytes.Length != info.Length || !TryReadEntries(bytes, out var entries)) continue;
                var matches = new List<(FrozenImage Image, ZipEntry Entry)>();
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryObjectHash(entry.Name, out var hash) || !expected.TryGetValue(hash, out var aliases)) continue;
                    var images = aliases.Where(image => !result.ContainsKey(image.Path) && image.Size == entry.RawSize).ToArray();
                    if (images.Length == 0 || !VerifyImage(bytes, entry, hash, cancellationToken)) continue;
                    foreach (var image in images) matches.Add((image, entry));
                }
                if (matches.Count == 0) continue;
                var integrity = "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes));
                var url = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
                foreach (var (image, entry) in matches)
                    result.TryAdd(image.Path, new WebPackagedAssemblySource(url, bytes.Length, integrity,
                        entry.Offset, entry.CompressedSize, entry.RawSize, image.Sha256, entry.Compression));
                acceptedBytes += bytes.Length;
            }
            catch (Exception error) when (IsUnverifiableInput(error)) { }
        }
        return result;
    }

    private static FrozenImage? ReadFrozenImage(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var fullPath = Path.GetFullPath(path);
            using var input = File.OpenRead(fullPath);
            if (input.Length is <= 0 or > MaximumImageBytes) return null;
            using (var pe = new PEReader(input, PEStreamOptions.LeaveOpen))
                if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null || !pe.GetMetadataReader().IsAssembly) return null;
            input.Position = 0;
            return new FrozenImage(fullPath, checked((int)input.Length), Convert.ToHexStringLower(SHA256.HashData(input)));
        }
        catch (Exception error) when (IsUnverifiableInput(error)) { return null; }
    }

    private static string[] EnumerateArchives(string root)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return [];
            return Directory.EnumerateFiles(root, "*.bassetbundle", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive
                }).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (IsUnverifiableInput(error)) { return []; }
    }

    // Parse the public ZIP records instead of relying on ZipArchive's private local-header offsets.
    // ZIP64, multi-disk, encryption and unsupported methods stay inline rather than being guessed.
    private static bool TryReadEntries(byte[] bytes, out List<ZipEntry> entries)
    {
        entries = [];
        if (bytes.Length < 22) return false;
        var end = -1;
        for (var candidate = bytes.Length - 22; candidate >= Math.Max(0, bytes.Length - 22 - ushort.MaxValue); candidate--)
            if (U32(bytes, candidate) == 0x06054b50 && candidate + 22L + U16(bytes, candidate + 20) == bytes.Length)
            { end = candidate; break; }
        if (end < 0 || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0) return false;
        var count = U16(bytes, end + 10);
        var centralSize = U32(bytes, end + 12);
        var centralOffset = U32(bytes, end + 16);
        if (count == ushort.MaxValue || count != U16(bytes, end + 8) || centralSize == uint.MaxValue ||
            centralOffset == uint.MaxValue || centralOffset + (long)centralSize != end) return false;
        if (end >= 20 && U32(bytes, end - 20) == 0x07064b50) return false;
        var cursor = checked((int)centralOffset);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ranges = new List<(int Start, int End)>();
        for (var index = 0; index < count; index++)
        {
            if (!Within(cursor, 46, end) || U32(bytes, cursor) != 0x02014b50) return false;
            var needed = U16(bytes, cursor + 6);
            var flags = U16(bytes, cursor + 8);
            var compression = U16(bytes, cursor + 10);
            var crc = U32(bytes, cursor + 16);
            var compressedSize = U32(bytes, cursor + 20);
            var rawSize = U32(bytes, cursor + 24);
            var nameSize = U16(bytes, cursor + 28);
            var extraSize = U16(bytes, cursor + 30);
            var commentSize = U16(bytes, cursor + 32);
            var localOffset = U32(bytes, cursor + 42);
            var recordSize = 46L + nameSize + extraSize + commentSize;
            if (needed > 20 || (flags & ~0x080e) != 0 || compression is not (0 or 8) ||
                (compression == 0 && (flags & 6) != 0) || U16(bytes, cursor + 34) != 0 ||
                compressedSize == uint.MaxValue || rawSize == uint.MaxValue || localOffset == uint.MaxValue ||
                !Within(cursor, recordSize, end) || !ValidExtra(bytes.AsSpan(cursor + 46 + nameSize, extraSize))) return false;
            var nameBytes = bytes.AsSpan(cursor + 46, nameSize);
            if ((flags & 0x0800) == 0 && nameBytes.ContainsAnyExceptInRange((byte)0, (byte)127)) return false;
            var name = StrictUtf8.GetString(nameBytes);
            if (!IsSafeRelativePath(name) || !names.Add(name)) return false;
            if (!TryReadLocal(bytes, checked((int)localOffset), checked((int)centralOffset), needed, flags,
                    compression, nameBytes, crc, compressedSize, rawSize, out var dataOffset, out var rangeEnd)) return false;
            ranges.Add((checked((int)localOffset), rangeEnd));
            if (!name.EndsWith('/')) entries.Add(new ZipEntry(name, dataOffset, checked((int)compressedSize),
                checked((int)rawSize), compression, crc));
            cursor = checked(cursor + (int)recordSize);
        }
        if (cursor != end) return false;
        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var i = 1; i < ranges.Count; i++) if (ranges[i].Start < ranges[i - 1].End) return false;
        return true;
    }

    private static bool TryReadLocal(byte[] bytes, int local, int central, ushort needed, ushort flags,
        ushort compression, ReadOnlySpan<byte> name, uint crc, uint compressedSize, uint rawSize,
        out int dataOffset, out int rangeEnd)
    {
        dataOffset = rangeEnd = 0;
        if (!Within(local, 30, central) || U32(bytes, local) != 0x04034b50 || U16(bytes, local + 4) != needed ||
            U16(bytes, local + 6) != flags || U16(bytes, local + 8) != compression) return false;
        var nameSize = U16(bytes, local + 26);
        var extraSize = U16(bytes, local + 28);
        var headerSize = 30L + nameSize + extraSize;
        if (nameSize != name.Length || !Within(local, headerSize, central) ||
            !bytes.AsSpan(local + 30, nameSize).SequenceEqual(name) ||
            !ValidExtra(bytes.AsSpan(local + 30 + nameSize, extraSize))) return false;
        var descriptor = (flags & 8) != 0;
        if ((!descriptor && (U32(bytes, local + 14) != crc || U32(bytes, local + 18) != compressedSize ||
                             U32(bytes, local + 22) != rawSize)) ||
            (descriptor && (!ZeroOrEqual(U32(bytes, local + 14), crc) ||
                            !ZeroOrEqual(U32(bytes, local + 18), compressedSize) ||
                            !ZeroOrEqual(U32(bytes, local + 22), rawSize)))) return false;
        dataOffset = checked(local + (int)headerSize);
        if (!Within(dataOffset, compressedSize, central) || (compression == 0 && compressedSize != rawSize)) return false;
        rangeEnd = checked(dataOffset + (int)compressedSize);
        if (!descriptor) return true;
        var fields = rangeEnd;
        if (!Within(fields, 12, central)) return false;
        if (U32(bytes, fields) == 0x08074b50) fields += 4;
        if (!Within(fields, 12, central) || U32(bytes, fields) != crc ||
            U32(bytes, fields + 4) != compressedSize || U32(bytes, fields + 8) != rawSize) return false;
        rangeEnd = fields + 12;
        return true;
    }

    private static bool VerifyImage(byte[] bytes, ZipEntry entry, string expectedHash, CancellationToken cancellationToken)
    {
        if (entry.RawSize is <= 0 or > MaximumImageBytes) return false;
        using var packed = new MemoryStream(bytes, entry.Offset, entry.CompressedSize, writable: false);
        using var inflated = entry.Compression == 8 ? new DeflateStream(packed, CompressionMode.Decompress, leaveOpen: true) : null;
        Stream input = inflated is null ? packed : inflated;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[16 * 1024];
        long count = 0;
        var crc = uint.MaxValue;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer);
            if (read == 0) break;
            if ((count += read) > entry.RawSize) return false;
            hash.AppendData(buffer.AsSpan(0, read));
            foreach (var value in buffer.AsSpan(0, read)) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        }
        return count == entry.RawSize && ~crc == entry.Crc &&
               Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.Ordinal);
    }

    private static bool ValidExtra(ReadOnlySpan<byte> extra)
    {
        while (!extra.IsEmpty)
        {
            if (extra.Length < 4) return false;
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            if (size > extra.Length - 4 || tag is 0x0001 or 0x0017 or 0x9901) return false;
            extra = extra[(4 + size)..];
        }
        return true;
    }

    private static bool TryObjectHash(string name, out string hash)
    {
        hash = string.Empty;
        if (name.Length != 76 || !name.StartsWith("objects/", StringComparison.Ordinal) ||
            !name.EndsWith(".bin", StringComparison.Ordinal)) return false;
        var value = name.AsSpan(8, 64);
        foreach (var character in value) if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        hash = value.ToString();
        return true;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] is '/' or '\\' || path.Contains('\\') || path.Contains(':') ||
            path.Any(character => char.IsControl(character))) return false;
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length; i++)
            if (parts[i] is "." or ".." || (parts[i].Length == 0 && i != parts.Length - 1)) return false;
        return true;
    }

    private static bool Within(int offset, long length, int limit) => offset >= 0 && length >= 0 && offset + length <= limit;
    private static bool ZeroOrEqual(uint value, uint expected) => value == 0 || value == expected;
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static bool IsUnverifiableInput(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        BadImageFormatException or ArgumentException or NotSupportedException or OverflowException or InvalidOperationException;

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? (value >> 1) ^ 0xedb88320u : value >> 1;
            table[index] = value;
        }
        return table;
    }

    private sealed record FrozenImage(string Path, int Size, string Sha256);
    private sealed record ZipEntry(string Name, int Offset, int CompressedSize, int RawSize, ushort Compression, uint Crc);
}

internal sealed record WebPackagedAssemblySource(string Path, int ArchiveSize, string ArchiveIntegrity,
    int Offset, int CompressedSize, int RawSize, string AssemblySha256, int Compression);
