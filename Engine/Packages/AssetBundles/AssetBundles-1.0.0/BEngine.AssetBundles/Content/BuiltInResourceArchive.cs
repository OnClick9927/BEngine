using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Platform;

namespace BEngine.Content;

internal sealed class BuiltInResourceArchive
{
    internal const string FileName = "aot.bresources";
    internal const int CurrentVersion = 3;

    private const int LegacyVersion = 1;
    private const int ObfuscatedVersion = 2;
    private const int LegacyHeaderSize = 64;
    private const int HeaderSize = 96;
    private const int HashSize = 32;
    private const int MaximumEntryCount = 100_000;
    private const int MaximumSettingsPerEntry = 256;
    private const int MaximumAddressBytes = 4096;
    private const int MaximumStringBytes = 16 * 1024;
    private const int MaximumIndexBytes = 128 * 1024 * 1024;
    private const long MaximumPayloadBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumEntryBytes = 512 * 1024 * 1024;
    private static readonly byte[] Magic = "BENGBRES"u8.ToArray();
    private static readonly byte[] IndexObfuscationDomain =
        "BEngine.BuiltInResourceArchive/v2/index"u8.ToArray();
    private static readonly byte[] PayloadObfuscationDomain =
        "BEngine.BuiltInResourceArchive/v2/payload"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _path;
    private readonly FileUtility _fileSystem;
    private readonly int _version;
    internal ContentEncryptionKey? EncryptionKey { get; }
    private readonly long _payloadStart;
    private readonly Dictionary<string, BuiltInResourceArchiveEntry> _entries;
    private readonly IReadOnlyList<BuiltInResourceArchiveEntry> _orderedEntries;

    private BuiltInResourceArchive(
        string path,
        FileUtility fileSystem,
        int version,
        long payloadStart,
        IEnumerable<BuiltInResourceArchiveEntry> entries,
        ContentEncryptionKey? encryptionKey)
    {
        _path = path;
        _fileSystem = fileSystem;
        _version = version;
        EncryptionKey = encryptionKey;
        _payloadStart = payloadStart;
        _orderedEntries = Array.AsReadOnly(entries.OrderBy(
            static entry => entry.Address, StringComparer.Ordinal).ToArray());
        _entries = _orderedEntries.ToDictionary(
            static entry => entry.Address, StringComparer.OrdinalIgnoreCase);
    }

    internal IReadOnlyList<BuiltInResourceArchiveEntry> Entries => _orderedEntries;

    internal static void Write(
        string path,
        IEnumerable<BuiltInResourceArchiveWriteEntry> entries,
        BCancellationToken cancellationToken = default,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        ContentEncryptionKey? encryptionKey = null,
        bool embedEncryptionKey = false,
        bool compress = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        var destination = pathService.GetFullPath(path);
        var ordered = entries.OrderBy(static entry => entry.Address, StringComparer.Ordinal).ToArray();
        ValidateWriteEntries(ordered, fileSystem, cancellationToken);

        var plans = CreatePayloadPlans(ordered);
        var version = compress ? CurrentVersion : ObfuscatedVersion;
        var indexBytes = BuildIndex(ordered, plans.ByAddress, version);
        if (compress && indexBytes.Length > MaximumIndexBytes)
        {
            compress = false;
            version = ObfuscatedVersion;
            indexBytes = BuildIndex(ordered, plans.ByAddress, version);
        }
        if (indexBytes.Length > MaximumIndexBytes)
            throw new InvalidDataException("Built-in resource archive index is too large.");
        var directory = pathService.GetDirectoryName(destination) ??
                        throw new InvalidDataException($"Built-in resource path has no parent: '{destination}'.");
        fileSystem.CreateDirectory(directory);
        var temporary = pathService.Combine(
            directory, $".{pathService.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = fileSystem.OpenFile(
                       temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                       64 * 1024, FileOptions.SequentialScan))
            {
                output.Write(new byte[HeaderSize]);
                output.Write(indexBytes);
                var payloadStart = output.Position;
                foreach (var payload in plans.Payloads)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    payload.Offset = output.Position - payloadStart;
                    WritePayload(payload, output, fileSystem, compress, cancellationToken);
                }
                var payloadLength = output.Position - payloadStart;
                // The v3 index adds nine bytes per address. Fall back for the whole archive
                // when compression cannot recover that cost, including already compressed assets.
                if (version == CurrentVersion &&
                    plans.Payloads.Sum(static payload => payload.Source.Size - payload.StoredSize) <= ordered.LongLength * 9)
                {
                    version = ObfuscatedVersion;
                    indexBytes = BuildIndex(ordered, plans.ByAddress, version);
                    output.Position = HeaderSize + indexBytes.Length;
                    payloadStart = output.Position;
                    foreach (var payload in plans.Payloads)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        payload.Offset = output.Position - payloadStart;
                        payload.Compressed = false;
                        WritePayload(payload, output, fileSystem, compress: false, cancellationToken);
                    }
                    payloadLength = output.Position - payloadStart;
                }
                output.SetLength(output.Position);
                indexBytes = BuildIndex(ordered, plans.ByAddress, version);
                var indexHash = SHA256.HashData(indexBytes);
                ApplyCounterKeystream(indexBytes, IndexObfuscationDomain, indexHash);
                output.Position = HeaderSize;
                output.Write(indexBytes);
                output.Flush();
                output.Position = HeaderSize;
                var bodyHash = SHA256.HashData(output);
                output.Position = 0;
                using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
                writer.Write(Magic);
                writer.Write((ushort)version);
                writer.Write((ushort)HeaderSize);
                writer.Write(ordered.Length);
                writer.Write((long)indexBytes.Length);
                writer.Write(payloadLength);
                writer.Write(bodyHash);
                writer.Write(indexHash);
                if (output.Position != HeaderSize)
                    throw new InvalidOperationException("Built-in resource archive header size is inconsistent.");
                fileSystem.Flush(output, flushToDisk: true);
            }
            if (encryptionKey is not null)
            {
                var encrypted = temporary + ".encrypted";
                try
                {
                    using (var source = fileSystem.OpenRead(temporary))
                    using (var output = fileSystem.OpenFile(encrypted, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                               64 * 1024, FileOptions.SequentialScan))
                    {
                        ContentEncryption.Write(source, output, encryptionKey, embedEncryptionKey,
                            () => cancellationToken.ThrowIfCancellationRequested());
                        fileSystem.Flush(output, flushToDisk: true);
                    }
                    fileSystem.MoveFile(encrypted, destination, overwrite: true);
                }
                finally { if (fileSystem.FileExists(encrypted)) fileSystem.DeleteFile(encrypted); }
            }
            else fileSystem.MoveFile(temporary, destination, overwrite: true);
        }
        finally
        {
            if (fileSystem.FileExists(temporary)) fileSystem.DeleteFile(temporary);
        }
    }

    internal static BuiltInResourceArchive Open(
        string path,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        ContentEncryptionKey? encryptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        var archivePath = pathService.GetFullPath(path);
        using var stream = ContentEncryption.OpenRead(fileSystem.OpenFile(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan), encryptionKey, encryptionKey is not null);
        if (stream.Length < LegacyHeaderSize)
            throw new InvalidDataException("Built-in resource archive is truncated.");
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Built-in resource archive magic is invalid.");
        var version = reader.ReadUInt16();
        if (version is not LegacyVersion and not ObfuscatedVersion && version != CurrentVersion)
            throw new InvalidDataException($"Unsupported built-in resource archive version {version}.");
        var expectedHeaderSize = version == LegacyVersion ? LegacyHeaderSize : HeaderSize;
        if (reader.ReadUInt16() != expectedHeaderSize)
            throw new InvalidDataException("Built-in resource archive header size is invalid.");
        var entryCount = reader.ReadInt32();
        var indexLength = reader.ReadInt64();
        var payloadLength = reader.ReadInt64();
        var expectedBodyHash = reader.ReadBytes(HashSize);
        var expectedIndexHash = version >= ObfuscatedVersion
            ? reader.ReadBytes(HashSize)
            : [];
        if (entryCount is <= 0 or > MaximumEntryCount ||
            indexLength is <= 0 or > MaximumIndexBytes ||
            payloadLength is < 0 or > MaximumPayloadBytes ||
            expectedBodyHash.Length != HashSize ||
            version >= ObfuscatedVersion && expectedIndexHash.Length != HashSize ||
            stream.Length != expectedHeaderSize + indexLength + payloadLength)
            throw new InvalidDataException("Built-in resource archive header values are invalid.");

        stream.Position = expectedHeaderSize;
        var actualBodyHash = SHA256.HashData(stream);
        if (!CryptographicOperations.FixedTimeEquals(expectedBodyHash, actualBodyHash))
            throw new InvalidDataException("Built-in resource archive failed its SHA-256 integrity check.");
        stream.Position = expectedHeaderSize;
        var indexBytes = reader.ReadBytes(checked((int)indexLength));
        if (indexBytes.Length != indexLength)
            throw new InvalidDataException("Built-in resource archive index is truncated.");
        if (version >= ObfuscatedVersion)
        {
            ApplyCounterKeystream(indexBytes, IndexObfuscationDomain, expectedIndexHash);
            var actualIndexHash = SHA256.HashData(indexBytes);
            if (!CryptographicOperations.FixedTimeEquals(expectedIndexHash, actualIndexHash))
                throw new InvalidDataException(
                    "Built-in resource archive index failed its SHA-256 integrity check.");
        }
        var parsed = ReadIndex(indexBytes, entryCount, payloadLength, version);
        return new BuiltInResourceArchive(
            archivePath, fileSystem, version, expectedHeaderSize + indexLength, parsed,
            ContentEncryption.GetKey(stream));
    }

    internal bool TryGetEntry(string address, out BuiltInResourceArchiveEntry entry)
    {
        var canonical = AssetBundleValidation.NormalizeAddress(address);
        if (_entries.TryGetValue(canonical, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    internal IReadOnlyList<string> EnumerateAddresses(string prefix = "")
    {
        var normalized = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.Replace('\\', '/').Trim('/');
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)) normalized = "Assets/";
        else if (normalized.Length != 0) normalized = AssetBundleValidation.NormalizeAddress(normalized);
        return Array.AsReadOnly(_orderedEntries
            .Where(entry => normalized.Length == 0 || entry.Address.StartsWith(
                normalized, StringComparison.OrdinalIgnoreCase))
            .Select(static entry => entry.Address)
            .ToArray());
    }

    internal byte[] ReadBytes(string address)
    {
        var entry = GetEntry(address);
        var bytes = new byte[checked((int)entry.Size)];
        using var stream = OpenPayload(entry);
        stream.ReadExactly(bytes);
        ValidateDecodedLength(stream, entry);
        VerifyPayload(entry, bytes);
        return bytes;
    }

    internal async BValueTask<byte[]> ReadBytesAsync(
        string address,
        BCancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = GetEntry(address);
        if (BackgroundWorkScheduler.IsSupported)
        {
            // OpenPayload creates a request-local stream. Brotli/decryption and
            // the checksum are pure data work and must not occupy a game frame.
            return await BackgroundWorkScheduler.Main.Run(token => ReadBytesOnWorker(entry, token), cancellationToken);
        }
        var bytes = new byte[checked((int)entry.Size)];
        using var stream = OpenPayload(entry);
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException("Built-in resource payload is truncated.");
            offset += read;
            if (offset < bytes.Length) await BValueTask.Yield();
        }
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDecodedLength(stream, entry);
        VerifyPayload(entry, bytes);
        return bytes;
    }

    private byte[] ReadBytesOnWorker(BuiltInResourceArchiveEntry entry, CancellationToken cancellationToken)
    {
        var bytes = new byte[checked((int)entry.Size)];
        using var stream = OpenPayload(entry);
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException("Built-in resource payload is truncated.");
            offset += read;
        }
        ValidateDecodedLength(stream, entry);
        VerifyPayload(entry, bytes);
        return bytes;
    }

    private Stream OpenPayload(BuiltInResourceArchiveEntry entry)
    {
        var source = ContentEncryption.OpenRead(_fileSystem.OpenFile(
            _path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.RandomAccess), EncryptionKey, EncryptionKey is not null);
        try
        {
            source.Position = checked(_payloadStart + entry.PayloadOffset);
            var payload = new PayloadReadStream(source, entry, _version >= ObfuscatedVersion);
            return entry.Compressed
                ? new BrotliStream(payload, CompressionMode.Decompress)
                : payload;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static void ValidateDecodedLength(Stream stream, BuiltInResourceArchiveEntry entry)
    {
        if (stream.ReadByte() != -1)
            throw new InvalidDataException(
                $"Built-in resource '{entry.Address}' exceeds its declared decoded size.");
    }

    private BuiltInResourceArchiveEntry GetEntry(string address) =>
        TryGetEntry(address, out var entry)
            ? entry
            : throw new FileNotFoundException(
                $"Built-in resource address '{address}' was not found in '{_path}'.", address);

    private static void ValidateWriteEntries(
        IReadOnlyList<BuiltInResourceArchiveWriteEntry> entries,
        FileUtility fileSystem,
        BCancellationToken cancellationToken)
    {
        if (entries.Count is <= 0 or > MaximumEntryCount)
            throw new InvalidDataException("A built-in resource archive requires 1-100000 entries.");
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!addresses.Add(entry.Address))
                throw new InvalidDataException($"Duplicate built-in resource address '{entry.Address}'.");
            ValidateMetadata(
                entry.Address, entry.AssetType, entry.Guid, entry.OwnerGuid, entry.LocalIdentifier,
                entry.Importer, entry.ImporterSettings, entry.Size, entry.Sha256);
            using var source = entry.OpenRead(fileSystem);
            VerifySource(source, entry, cancellationToken);
        }
    }

    private static PayloadPlans CreatePayloadPlans(
        IReadOnlyList<BuiltInResourceArchiveWriteEntry> entries)
    {
        var payloads = new List<PayloadPlan>();
        var byKey = new Dictionary<string, PayloadPlan>(StringComparer.Ordinal);
        var byAddress = new Dictionary<string, PayloadPlan>(StringComparer.OrdinalIgnoreCase);
        long offset = 0;
        foreach (var entry in entries)
        {
            var key = $"{entry.Sha256}\0{entry.Size}";
            if (!byKey.TryGetValue(key, out var payload))
            {
                payload = new PayloadPlan(entry, offset);
                byKey.Add(key, payload);
                payloads.Add(payload);
                offset = checked(offset + entry.Size);
                if (offset > MaximumPayloadBytes)
                    throw new InvalidDataException("Built-in resource archive payload is too large.");
            }
            byAddress.Add(entry.Address, payload);
        }
        return new PayloadPlans(payloads, byAddress);
    }

    private static byte[] BuildIndex(
        IReadOnlyList<BuiltInResourceArchiveWriteEntry> entries,
        IReadOnlyDictionary<string, PayloadPlan> payloads,
        int version)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var entry in entries)
        {
            WriteString(writer, entry.Address, MaximumAddressBytes, "address");
            writer.Write(entry.Guid.ToByteArray());
            writer.Write(entry.OwnerGuid.ToByteArray());
            writer.Write(entry.LocalIdentifier);
            WriteString(writer, entry.AssetType, MaximumStringBytes, "asset type");
            WriteString(writer, entry.Importer, MaximumStringBytes, "importer");
            writer.Write(entry.ImporterSettings.Count);
            foreach (var setting in entry.ImporterSettings.OrderBy(
                         static pair => pair.Key, StringComparer.Ordinal))
            {
                WriteString(writer, setting.Key, MaximumStringBytes, "importer setting name");
                WriteString(writer, setting.Value, MaximumStringBytes, "importer setting value");
            }
            writer.Write(payloads[entry.Address].Offset);
            writer.Write(entry.Size);
            if (version >= CurrentVersion)
            {
                writer.Write(payloads[entry.Address].StoredSize);
                writer.Write((byte)(payloads[entry.Address].Compressed ? 1 : 0));
            }
            writer.Write(Convert.FromHexString(entry.Sha256));
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static BuiltInResourceArchiveEntry[] ReadIndex(
        byte[] indexBytes,
        int entryCount,
        long payloadLength,
        int version)
    {
        using var stream = new MemoryStream(indexBytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var entries = new BuiltInResourceArchiveEntry[entryCount];
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Length; index++)
        {
            var address = ReadString(reader, MaximumAddressBytes, "address");
            var guid = new Guid(ReadExactBytes(reader, 16, "resource GUID"));
            var ownerGuid = new Guid(ReadExactBytes(reader, 16, "resource owner GUID"));
            var localIdentifier = reader.ReadInt64();
            var assetType = ReadString(reader, MaximumStringBytes, "asset type");
            var importer = ReadString(reader, MaximumStringBytes, "importer");
            var settingsCount = reader.ReadInt32();
            if (settingsCount is < 0 or > MaximumSettingsPerEntry)
                throw new InvalidDataException("Built-in resource importer setting count is invalid.");
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var settingIndex = 0; settingIndex < settingsCount; settingIndex++)
            {
                var key = ReadString(reader, MaximumStringBytes, "importer setting name");
                var value = ReadString(reader, MaximumStringBytes, "importer setting value");
                if (!settings.TryAdd(key, value))
                    throw new InvalidDataException(
                        $"Built-in resource '{address}' contains duplicate importer setting '{key}'.");
            }
            var offset = reader.ReadInt64();
            var size = reader.ReadInt64();
            var storedSize = version >= CurrentVersion ? reader.ReadInt64() : size;
            var compression = version >= CurrentVersion ? reader.ReadByte() : 0;
            var sha256 = Convert.ToHexString(ReadExactBytes(reader, HashSize, "payload hash"))
                .ToLowerInvariant();
            var canonical = AssetBundleValidation.NormalizeAddress(address);
            if (!canonical.Equals(address, StringComparison.Ordinal) || !addresses.Add(address))
                throw new InvalidDataException($"Built-in resource address '{address}' is invalid or duplicated.");
            ValidateMetadata(
                address, assetType, guid, ownerGuid, localIdentifier,
                importer, settings, size, sha256);
            if (compression is not 0 and not 1 || storedSize < 0 ||
                compression == 0 && storedSize != size ||
                compression == 1 && (size == 0 || storedSize == 0 || storedSize >= size) ||
                offset < 0 || offset > payloadLength || storedSize > payloadLength - offset)
                throw new InvalidDataException($"Built-in resource '{address}' payload range is invalid.");
            entries[index] = new BuiltInResourceArchiveEntry(
                address, assetType, guid, ownerGuid, localIdentifier,
                importer, settings, offset, size, sha256, storedSize, compression == 1);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Built-in resource archive index contains trailing data.");
        ValidatePayloadRanges(entries, payloadLength);
        return entries;
    }

    private static void ValidatePayloadRanges(
        IEnumerable<BuiltInResourceArchiveEntry> entries,
        long payloadLength)
    {
        var ranges = entries.GroupBy(
                static entry => (entry.PayloadOffset, entry.StoredSize, entry.Size, entry.Sha256, entry.Compressed))
            .Select(static group => group.Key)
            .OrderBy(static range => range.PayloadOffset)
            .ThenBy(static range => range.StoredSize)
            .ToArray();
        long expectedOffset = 0;
        foreach (var range in ranges)
        {
            if (range.PayloadOffset != expectedOffset)
                throw new InvalidDataException("Built-in resource archive payload ranges overlap or contain gaps.");
            expectedOffset = checked(expectedOffset + range.StoredSize);
        }
        if (expectedOffset != payloadLength)
            throw new InvalidDataException("Built-in resource archive payload length is inconsistent.");
    }

    private static void ValidateMetadata(
        string address,
        string assetType,
        Guid guid,
        Guid ownerGuid,
        long localIdentifier,
        string importer,
        IReadOnlyDictionary<string, string> settings,
        long size,
        string sha256)
    {
        if (Encoding.UTF8.GetByteCount(address) > MaximumAddressBytes ||
            string.IsNullOrWhiteSpace(assetType) || Encoding.UTF8.GetByteCount(assetType) > MaximumStringBytes ||
            Encoding.UTF8.GetByteCount(importer) > MaximumStringBytes ||
            guid == Guid.Empty || ownerGuid == Guid.Empty || localIdentifier < 0 ||
            size is < 0 or > MaximumEntryBytes ||
            sha256.Length != HashSize * 2 || sha256.Any(static character => !char.IsAsciiHexDigit(character)))
            throw new InvalidDataException($"Built-in resource metadata is invalid for '{address}'.");
        if (localIdentifier == 0 && ownerGuid != guid)
            throw new InvalidDataException($"Main built-in resource '{address}' must own its payload identity.");
        if (settings.Count > MaximumSettingsPerEntry || settings.Any(static setting =>
                string.IsNullOrWhiteSpace(setting.Key) ||
                Encoding.UTF8.GetByteCount(setting.Key) > MaximumStringBytes ||
                Encoding.UTF8.GetByteCount(setting.Value) > MaximumStringBytes))
            throw new InvalidDataException($"Built-in resource importer settings are invalid for '{address}'.");
    }

    private static void CopyAndVerify(
        Stream source,
        Stream destination,
        BuiltInResourceArchiveWriteEntry entry,
        bool obfuscate,
        BCancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var obfuscationSeed = obfuscate
            ? CreatePayloadObfuscationSeed(entry.Sha256, entry.Size)
            : null;
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                if (obfuscationSeed is not null)
                    ApplyCounterKeystream(
                        buffer.AsSpan(0, read), PayloadObfuscationDomain, obfuscationSeed, total);
                destination.Write(buffer, 0, read);
                total = checked(total + read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        VerifySnapshot(entry, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void WritePayload(
        PayloadPlan payload,
        Stream output,
        FileUtility fileSystem,
        bool compress,
        BCancellationToken cancellationToken)
    {
        var start = output.Position;
        if (compress && payload.Source.Size >= 256)
        {
            using (var source = payload.Source.OpenRead(fileSystem))
            using (var compressor = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
                CopyAndVerify(source, compressor, payload.Source, obfuscate: false, cancellationToken);
            var compressedSize = output.Position - start;
            // Small savings do not justify decompression and codec overhead at load time.
            if (compressedSize <= payload.Source.Size - 32)
            {
                payload.Compressed = true;
                payload.StoredSize = compressedSize;
                var seed = CreatePayloadObfuscationSeed(payload.Source.Sha256, payload.Source.Size);
                var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    long offset = 0;
                    while (offset < compressedSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var count = (int)Math.Min(buffer.Length, compressedSize - offset);
                        output.Position = start + offset;
                        output.ReadExactly(buffer.AsSpan(0, count));
                        ApplyCounterKeystream(buffer.AsSpan(0, count), PayloadObfuscationDomain, seed, offset);
                        output.Position = start + offset;
                        output.Write(buffer, 0, count);
                        offset += count;
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
                return;
            }
            output.Position = start;
        }
        using var original = payload.Source.OpenRead(fileSystem);
        CopyAndVerify(original, output, payload.Source, obfuscate: true, cancellationToken);
        payload.StoredSize = payload.Source.Size;
    }

    private static byte[] CreatePayloadObfuscationSeed(string sha256, long size)
    {
        var seed = new byte[HashSize + sizeof(long)];
        Convert.FromHexString(sha256).CopyTo(seed, 0);
        BinaryPrimitives.WriteInt64LittleEndian(seed.AsSpan(HashSize), size);
        return seed;
    }

    private static void ApplyCounterKeystream(
        Span<byte> bytes,
        ReadOnlySpan<byte> domain,
        ReadOnlySpan<byte> seed,
        long streamOffset = 0)
    {
        if (streamOffset < 0) throw new ArgumentOutOfRangeException(nameof(streamOffset));

        // This deterministic transform prevents loose plaintext disclosure. It is intentionally
        // obfuscation, not encryption: the algorithm and all seed material ship with the Player.
        Span<byte> counterInput = stackalloc byte[domain.Length + seed.Length + sizeof(ulong)];
        domain.CopyTo(counterInput);
        seed.CopyTo(counterInput[domain.Length..]);
        Span<byte> keyStream = stackalloc byte[HashSize];
        var counterPosition = counterInput.Length - sizeof(ulong);
        var block = checked((ulong)(streamOffset / HashSize));
        var blockOffset = (int)(streamOffset % HashSize);
        var position = 0;
        while (position < bytes.Length)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(counterInput[counterPosition..], block);
            SHA256.HashData(counterInput, keyStream);
            var count = Math.Min(HashSize - blockOffset, bytes.Length - position);
            for (var index = 0; index < count; index++)
                bytes[position + index] ^= keyStream[blockOffset + index];
            position += count;
            block = checked(block + 1);
            blockOffset = 0;
        }
    }

    private static void VerifySource(
        Stream source,
        BuiltInResourceArchiveWriteEntry entry,
        BCancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                total = checked(total + read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        VerifySnapshot(entry, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void VerifySnapshot(
        BuiltInResourceArchiveWriteEntry entry,
        long actualSize,
        string actualSha256)
    {
        if (actualSize != entry.Size || !actualSha256.Equals(entry.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Built-in resource '{entry.Address}' changed after its input snapshot was captured.");
    }

    private static void VerifyPayload(BuiltInResourceArchiveEntry entry, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != entry.Size ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Built-in resource '{entry.Address}' failed its SHA-256 integrity check.");
    }

    private static void WriteString(BinaryWriter writer, string value, int maximumBytes, string field)
    {
        if (value is null) throw new InvalidDataException($"Built-in resource {field} cannot be null.");
        var bytes = StrictUtf8.GetBytes(value);
        if (bytes.Length > maximumBytes)
            throw new InvalidDataException($"Built-in resource {field} is too long.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader, int maximumBytes, string field)
    {
        var length = reader.ReadInt32();
        if (length is < 0 || length > maximumBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException($"Built-in resource {field} length is invalid.");
        try { return StrictUtf8.GetString(ReadExactBytes(reader, length, field)); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Built-in resource {field} is not valid UTF-8.", exception);
        }
    }

    private static byte[] ReadExactBytes(BinaryReader reader, int length, string field)
    {
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new InvalidDataException($"Built-in resource archive ended while reading {field}.");
        return bytes;
    }

    private sealed class PayloadPlan(BuiltInResourceArchiveWriteEntry source, long offset)
    {
        internal BuiltInResourceArchiveWriteEntry Source { get; } = source;
        internal long Offset { get; set; } = offset;
        internal long StoredSize { get; set; } = source.Size;
        internal bool Compressed { get; set; }
    }

    // Decode only the selected entry. Never allocate the whole compressed payload or archive,
    // and never permit a decoder to consume bytes belonging to another resource.
    private sealed class PayloadReadStream(
        Stream source, BuiltInResourceArchiveEntry entry, bool obfuscated) : Stream
    {
        private readonly byte[]? _seed = obfuscated
            ? CreatePayloadObfuscationSeed(entry.Sha256, entry.Size)
            : null;
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => entry.StoredSize;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int ReadByte()
        {
            Span<byte> value = stackalloc byte[1];
            return Read(value) == 0 ? -1 : value[0];
        }

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, entry.StoredSize - _position);
            if (count == 0) return 0;
            var read = source.Read(buffer[..count]);
            if (read == 0) throw new EndOfStreamException("Built-in resource payload is truncated.");
            if (_seed is not null)
                ApplyCounterKeystream(buffer[..read], PayloadObfuscationDomain, _seed, _position);
            _position += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed record PayloadPlans(
        IReadOnlyList<PayloadPlan> Payloads,
        IReadOnlyDictionary<string, PayloadPlan> ByAddress);
}
