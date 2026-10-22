using System.Buffers.Binary;
using BEngine.Platform;
using BEngine.Rendering;

namespace BEngine.Editor;

internal static class MobileTextureEncoder
{
    internal static byte[] Encode(int width, int height, byte[] rgba, TextureCompressionFormat format,
        bool sRgb, CancellationToken cancellationToken)
    {
        var files = FileUtility.Current; var paths = PathUtility.Current;
        var platform = RuntimeServiceAccess.PlatformService;
        var configured = platform.GetEnvironmentVariable("BENGINE_BASISU_PATH");
        var executable = !string.IsNullOrWhiteSpace(configured) ? paths.GetFullPath(configured) :
            ResolveBundledTool();
        if (executable is null || !files.FileExists(executable))
            throw new FileNotFoundException("ETC2/ASTC import requires basisu 2.50. The Windows editor includes it; other hosts can set BENGINE_BASISU_PATH.");
        var directory = paths.Combine(platform.TemporaryDirectory, "BEngine.Texture." + Guid.NewGuid().ToString("N"));
        files.CreateDirectory(directory);
        try
        {
            var source = paths.Combine(directory, "image.png");
            files.WriteAllBytes(source, PngImageCodec.EncodeRgba(width, height, rgba));
            Run(["-uastc", "-uastc_level", "2", "-ktx2", "-no_multithreading", sRgb ? "-srgb" : "-linear",
                "-file", source, "-output_file", paths.Combine(directory, "image.ktx2")]);
            Run(["-unpack", "-file", paths.Combine(directory, "image.ktx2"), "-format_only",
                format == TextureCompressionFormat.Etc2Rgba8 ? "1" : "10", "-ktx_only"]);
            var suffix = format == TextureCompressionFormat.Etc2Rgba8 ? "ETC2_RGBA" : "ASTC_LDR_4X4_RGBA";
            var bytes = files.ReadAllBytes(paths.Combine(directory, $"image_transcoded_{suffix}_layer_0000.ktx"));
            return ReadKtx(bytes, width, height, format);
        }
        finally { files.DeleteDirectory(directory, recursive: true); }

        void Run(string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ProcessRunRequest(executable) { WorkingDirectory = directory };
            foreach (var argument in arguments) request.Arguments.Add(argument);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var result = ProcessUtility.Current.Run(request, timeout.Token);
            if (result.ExitCode != 0) throw new InvalidDataException($"Texture encoder failed ({result.ExitCode}): {result.StandardError}");
        }
    }

    private static string? ResolveBundledTool()
    {
        var platform = RuntimeServiceAccess.PlatformService;
        var relative = platform.IsWindows ? "Tools/TextureCompression/win-x64/basisu.exe" : "Tools/TextureCompression/basisu";
        if (EditorResource.FindPath(relative) is { } registered) return registered;
        // CLI builds can run before editor resource roots are installed. Locate either the
        // exported Editor directory or the source checkout relative to the host executable.
        var paths = PathUtility.Current; var files = FileUtility.Current;
        foreach (var start in new[] { platform.BaseDirectory, platform.CurrentDirectory })
        for (var directory = paths.GetFullPath(start); directory is not null;
             directory = paths.GetDirectoryName(directory))
        {
            foreach (var root in new[]
                     {
                         paths.Combine(directory, "Editor"),
                         paths.Combine(directory, "Engine", "src", "Core", "Editor"),
                         paths.Combine(directory, "src", "Core", "Editor")
                     })
            {
                var candidate = paths.Combine(root, relative);
                if (files.FileExists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static byte[] ReadKtx(ReadOnlySpan<byte> data, int width, int height, TextureCompressionFormat format)
    {
        ReadOnlySpan<byte> magic = [0xAB,0x4B,0x54,0x58,0x20,0x31,0x31,0xBB,0x0D,0x0A,0x1A,0x0A];
        if (data.Length < 68 || !data[..12].SequenceEqual(magic) || U32(data,12) != 0x04030201 ||
            U32(data,16) != 0 || U32(data,20) != 1 || U32(data,24) != 0 || U32(data,36) != width ||
            U32(data,40) != height || U32(data,44) != 0 || U32(data,48) != 0 || U32(data,52) != 1 || U32(data,56) != 1)
            throw new InvalidDataException("Texture encoder returned an invalid KTX image.");
        var internalFormat = U32(data,28);
        if (format == TextureCompressionFormat.Etc2Rgba8 ? internalFormat is not (0x9278 or 0x9279) :
            internalFormat is not (0x93B0 or 0x93D0)) throw new InvalidDataException("Texture encoder returned the wrong GPU format.");
        var offset = checked(64 + (int)U32(data,60));
        var expected = TextureImageData.LevelSize(width,height,format);
        if (offset > data.Length - 4 || U32(data,offset) != expected || data.Length - offset - 4 != expected)
            throw new InvalidDataException("Texture encoder returned an incomplete block payload.");
        return data.Slice(offset+4,expected).ToArray();
    }
    private static uint U32(ReadOnlySpan<byte> bytes,int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
