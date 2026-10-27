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

    internal static void Apply(string webRoot, FileUtility files, PathUtility paths)
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

        using var payload = new MemoryStream();
        var entries = new List<BundleEntry>();
        foreach (var input in inputs)
        {
            var name = paths.GetRelativePath(frameworkRoot, input).Replace('\\', '/');
            var bytes = files.ReadAllBytes(input);
            var offset = payload.Position;
            payload.Write(bytes);
            entries.Add(new(name, offset, bytes.Length,
                "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes))));
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var header = JsonSerializer.SerializeToUtf8Bytes(new { version = 3, compression = "brotli", size = payload.Length, entries }, options);
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
}
