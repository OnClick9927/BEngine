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
        IReadOnlyCollection<string>? packagedAssemblyPaths = null)
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
        if (!inputs.Any(path => paths.GetRelativePath(frameworkRoot, path) == "dotnet.js"))
            throw new InvalidDataException("The SDK's Web runtime entry point is missing.");

        // Reuse canonical images only when they already exist, byte for byte,
        // in a packaged HTTP AssetBundle. Never move runtime bytes into content.
        var sources = WebPackagedAssemblySources.Discover(webRoot, packagedAssemblyPaths ?? []);
        var sourcesByName = sources.GroupBy(static pair => Path.GetFileNameWithoutExtension(pair.Key), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(static pair => pair.Value.AssemblySha256).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var archives = new List<ArchiveSource>();
        var archiveIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var externalEntries = new List<ExternalEntry>();
        using var payload = new MemoryStream();
        var entries = new List<BundleEntry>();
        foreach (var input in inputs)
        {
            var name = paths.GetRelativePath(frameworkRoot, input).Replace('\\', '/');
            var bytes = files.ReadAllBytes(input);
            var logicalName = Path.GetFileNameWithoutExtension(name);
            var fingerprintSeparator = logicalName.LastIndexOf('.');
            if (fingerprintSeparator > 0 &&
                !sourcesByName.ContainsKey(logicalName) &&
                logicalName[(fingerprintSeparator + 1)..].All(char.IsAsciiLetterOrDigit))
                logicalName = logicalName[..fingerprintSeparator];
            if (name.EndsWith(".wasm", StringComparison.Ordinal) &&
                sourcesByName.TryGetValue(logicalName, out var image))
            {
                var source = image.Value;
                var original = files.ReadAllBytes(image.Key);
                var delta = original.Length == source.RawSize &&
                    Convert.ToHexStringLower(SHA256.HashData(original)) == source.AssemblySha256
                    ? WebRuntimeBinaryDelta.Create(original, bytes) : [];
                if (delta.Length > 0 && delta.Length + 512 < bytes.Length)
                {
                    if (!WebRuntimeBinaryDelta.Apply(original, delta, bytes.Length).AsSpan().SequenceEqual(bytes))
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
        var descriptor = JsonSerializer.Serialize(new
        {
            url = "./" + bundleName + "?benginev=" + Convert.ToHexStringLower(hash),
            integrity = "sha256-" + Convert.ToBase64String(hash),
            bytes = packed.Length,
            rawBytes = entries.Sum(entry => (long)entry.Size),
            files = entries.Count,
            externalFiles = externalEntries.Count,
            archiveBytes = archives.Sum(static archive => archive.Size),
            decoder = Convert.ToBase64String(decoder),
            decoderIntegrity = "sha256-" + Convert.ToBase64String(SHA256.HashData(decoder))
        }, options);

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
