using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Packages the SDK's published runtime without rewriting managed/AOT images.</summary>
internal static class WebPlayerRuntimeBundle
{
    private const string DescriptorMarker = "/* BENGINE_RUNTIME_BUNDLE */ null";

    internal static void Apply(string webRoot, FileUtility files, PathUtility paths,
        IReadOnlyDictionary<string, PlatformPackagedAssemblySource>? packagedAssemblySources = null)
    {
        var frameworkRoot = paths.Combine(webRoot, "_framework");
        var loaderPath = paths.Combine(webRoot, "framework.js");
        var decoderPath = paths.Combine(webRoot, "brotli-decoder.wasm");
        var decoder = files.ReadAllBytes(decoderPath);
        if (decoder.Length is < 8 or > 1024 * 1024 || !decoder.AsSpan(0, 8).SequenceEqual("\0asm\x01\0\0\0"u8))
            throw new InvalidDataException("The Web Brotli decoder is missing or invalid.");
        var loader = files.ReadAllText(loaderPath);
        if (loader.Split(DescriptorMarker, StringSplitOptions.None).Length != 2)
            throw new InvalidDataException("The Web runtime bundle loader has no unique descriptor marker.");

        var inputs = files.EnumerateFiles(frameworkRoot, "*", SearchOption.AllDirectories)
            .Where(path => paths.GetExtension(path) is ".wasm" or ".dat" or ".js")
            .OrderBy(path => paths.GetRelativePath(frameworkRoot, path), StringComparer.Ordinal)
            .ToArray();
        if (!Query1605())
            throw new InvalidDataException("The SDK's Web runtime entry point is missing.");
        bool Query1605()
        {
            var querySource1605 = inputs;
            ArgumentNullException.ThrowIfNull(querySource1605, "source");
            if (querySource1605.GetType() == typeof(string[]) || querySource1605.GetType() == typeof(global::System.Collections.Generic.List<string>))
            {
                global::System.ReadOnlySpan<string> queryValues1605 = querySource1605.GetType() == typeof(string[]) ? (string[])(object)querySource1605 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource1605);
                foreach (var path in queryValues1605)
                {
                    if (paths.GetRelativePath(frameworkRoot, path) == "dotnet.js")
                        return true;
                }
            }
            else
            {
                foreach (var path in querySource1605)
                {
                    if (paths.GetRelativePath(frameworkRoot, path) == "dotnet.js")
                        return true;
                }
            }
        
            return false;
        }

        // Reuse canonical images only when they already exist, byte for byte,
        // in a packaged HTTP archive. Never move runtime bytes into content.
        var sources = packagedAssemblySources ?? new Dictionary<string, PlatformPackagedAssemblySource>(StringComparer.OrdinalIgnoreCase);
        var sourcesByName = BuildIndex3170(sources.GroupBy(static pair => Path.GetFileNameWithoutExtension(pair.Key), StringComparer.OrdinalIgnoreCase).Where(static group => group.Select(static pair => pair.Value.AssemblySha256).Distinct(StringComparer.Ordinal).Count() == 1), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>> BuildIndex3170(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>> indexSource3170, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer3170)
        {
            ArgumentNullException.ThrowIfNull(indexSource3170, "source");
            var knownCount = indexSource3170.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>(indexComparer3170);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>> values;
            if (knownCount && indexSource3170 is global::System.Linq.IGrouping<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>[] array)
                values = array;
            else if (knownCount && indexSource3170 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>(capacity, indexComparer3170);
                foreach (var item in indexSource3170)
                    result.Add(IndexKey3170(item), IndexValue3170(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>>(values.Length, indexComparer3170);
                foreach (var item in values)
                    result.Add(IndexKey3170(item), IndexValue3170(item));
                return result;
            }
        
            string IndexKey3170(global::System.Linq.IGrouping<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>> group) => group.Key;
            global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource> IndexValue3170(global::System.Linq.IGrouping<string, global::System.Collections.Generic.KeyValuePair<string, global::BEngine.Editor.PlatformPackagedAssemblySource>> group) => group.First();
        }
        var archives = new List<ArchiveSource>();
        var archiveIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var externalEntries = new List<ExternalEntry>();
        using var payload = new MemoryStream();
        var entries = new List<BundleEntry>();
        foreach (var input in inputs)
        {
            var name = paths.GetRelativePath(frameworkRoot, input).Replace('\\', '/');
            var logicalName = Path.GetFileNameWithoutExtension(name);
            var fingerprintSeparator = logicalName.LastIndexOf('.');
            if (fingerprintSeparator > 0 && !sourcesByName.ContainsKey(logicalName) && Scan7263(logicalName[(fingerprintSeparator + 1)..], char.IsAsciiLetterOrDigit))
                logicalName = logicalName[..fingerprintSeparator];
            [global::BEngine.SkipLoopGuard]
            bool Scan7263(string scanSource7263, global::System.Func<char, bool> scanPredicate7263)
            {
                ArgumentNullException.ThrowIfNull(scanSource7263, "source");
                ArgumentNullException.ThrowIfNull(scanPredicate7263, "predicate");
                foreach (var scanItem7263 in scanSource7263)
                {
                    if (!scanPredicate7263(scanItem7263))
                        return false;
                }
            
                return true;
            }
            if (!name.EndsWith(".wasm", StringComparison.Ordinal) ||
                !sourcesByName.TryGetValue(logicalName, out var image))
            {
                entries.Add(AppendFile(payload, input, name, files));
                continue;
            }
            var bytes = files.ReadAllBytes(input);
            var source = image.Value;
            var original = files.ReadAllBytes(image.Key);
            var delta = original.Length == source.RawSize &&
                Convert.ToHexStringLower(SHA256.HashData(original)) == source.AssemblySha256
                ? BinaryDelta.Create(original, bytes) : [];
            if (delta.Length > 0 && delta.Length + 512 < bytes.Length)
            {
                if (!BinaryDelta.Apply(original, delta, bytes.Length).AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("Web runtime image reconstruction failed: " + name);
                if (!archiveIndices.TryGetValue(source.Path, out var archiveIndex))
                {
                    archiveIndex = archives.Count;
                    archiveIndices.Add(source.Path, archiveIndex);
                    archives.Add(new(source.Path, source.ArchiveSize, source.ArchiveIntegrity));
                }
                var deltaName = "bengine-delta/" + name + ".bin";
                externalEntries.Add(new(name, bytes.Length, Integrity(bytes), deltaName,
                    archiveIndex, source.Offset, source.CompressedSize, source.RawSize,
                    "sha256-" + Convert.ToBase64String(Convert.FromHexString(source.AssemblySha256)),
                    source.Compression));
                name = deltaName;
                bytes = delta;
            }
            var offset = payload.Position;
            payload.Write(bytes);
            entries.Add(new(name, offset, bytes.Length, Integrity(bytes)));
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var header = JsonSerializer.SerializeToUtf8Bytes(new { version = 4, compression = "brotli",
            size = payload.Length, entries, archives, externalEntries }, options);
        using var bundle = new MemoryStream();
        bundle.Write("BENGINE3"u8);
        Span<byte> headerLength = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(headerLength, header.Length);
        bundle.Write(headerLength);
        bundle.Write(header);
        // Share the compression dictionary across assemblies and remove per-file
        // compression wrappers. The 16 MiB window also finds repetitions beyond
        // BrotliStream's default 4 MiB window in the large native AOT module.
        CompressPayload(payload, bundle);
        var packed = bundle.ToArray();
        var hash = SHA256.HashData(packed);
        const string bundleName = "framework.bin";
        var descriptor = JsonSerializer.Serialize(new { url = "./" + bundleName + "?benginev=" + Convert.ToHexStringLower(hash), integrity = "sha256-" + Convert.ToBase64String(hash), bytes = packed.Length, rawBytes = Scan10543(entries), files = entries.Count, externalFiles = externalEntries.Count, archiveBytes = Scan10696(archives), decoder = Convert.ToBase64String(decoder), decoderIntegrity = "sha256-" + Convert.ToBase64String(SHA256.HashData(decoder)) }, options);
        [global::BEngine.SkipLoopGuard]
        long Scan10696(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.WebPlayerRuntimeBundle.ArchiveSource> scanSource10696)
        {
            ArgumentNullException.ThrowIfNull(scanSource10696, "source");
            long total = 0;
            foreach (var scanItem10696 in scanSource10696)
                total = checked(total + Match10696(scanItem10696));
            return total;
            long Match10696(global::BEngine.Editor.WebPlayerRuntimeBundle.ArchiveSource archive) => archive.Size;
        }
        
        [global::BEngine.SkipLoopGuard]
        long Scan10543(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.WebPlayerRuntimeBundle.BundleEntry> scanSource10543)
        {
            ArgumentNullException.ThrowIfNull(scanSource10543, "source");
            long total = 0;
            foreach (var scanItem10543 in scanSource10543)
                total = checked(total + Match10543(scanItem10543));
            return total;
            long Match10543(global::BEngine.Editor.WebPlayerRuntimeBundle.BundleEntry entry) => (long)entry.Size;
        }

        // Commit the complete pack and loader before removing their source files.
        files.WriteAllBytes(paths.Combine(webRoot, bundleName), packed);
        files.WriteAllText(loaderPath, loader.Replace(DescriptorMarker, descriptor, StringComparison.Ordinal));
        files.DeleteFile(decoderPath);
        foreach (var suffix in new[] { ".br", ".gz" })
        {
            var sidecar = decoderPath + suffix;
            if (files.FileExists(sidecar)) files.DeleteFile(sidecar);
        }
        files.DeleteDirectory(frameworkRoot, recursive: true);
    }

    private static string Integrity(ReadOnlySpan<byte> bytes) =>
        "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes));

    private static BundleEntry AppendFile(MemoryStream payload, string input, string name, FileUtility files)
    {
        using var stream = files.OpenRead(input);
        var offset = payload.Position;
        // Reserve as for one whole-file write, without keeping a second copy of
        // large AOT images alive or growing the payload for each read block.
        if (stream.CanSeek)
        {
            var required = checked((int)(offset + stream.Length - stream.Position));
            if (required > payload.Capacity)
                payload.Capacity = (int)Math.Max(required,
                    Math.Min(Array.MaxLength, Math.Max(256L, 2L * payload.Capacity)));
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[16 * 1024];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            var chunk = buffer[..read];
            payload.Write(chunk);
            hash.AppendData(chunk);
        }
        return new(name, offset, checked((int)(payload.Position - offset)),
            "sha256-" + Convert.ToBase64String(hash.GetHashAndReset()));
    }

    private static void CompressPayload(MemoryStream payload, Stream output)
    {
        using var encoder = new BrotliEncoder(quality: 11, window: 24);
        var remaining = payload.GetBuffer().AsSpan(0, checked((int)payload.Length));
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var status = encoder.Compress(remaining, buffer, out var consumed, out var written, isFinalBlock: true);
            output.Write(buffer.AsSpan(0, written));
            remaining = remaining[consumed..];
            if (status == System.Buffers.OperationStatus.Done && remaining.IsEmpty) return;
            if (status != System.Buffers.OperationStatus.DestinationTooSmall || (consumed == 0 && written == 0))
                throw new InvalidDataException("Unable to finish compressing the Web runtime bundle.");
        }
    }

    private sealed record BundleEntry(string Name, long Offset, int Size, string Integrity);
    private sealed record ArchiveSource(string Path, long Size, string Integrity);
    private sealed record ExternalEntry(string Name, int Size, string Integrity, string Delta,
        int Archive, long Offset, long CompressedSize, long SourceSize, string SourceIntegrity, int Compression);
}
