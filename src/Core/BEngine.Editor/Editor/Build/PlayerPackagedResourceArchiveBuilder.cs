using System.Security.Cryptography;
using BEngine.Content;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

internal static class PlayerPackagedResourceArchiveBuilder
{
    private static readonly string[] RuntimeCoreResourceDirectories =
    [
        "Icons",
        Path.Combine("Shaders", "PortableScene")
    ];

    internal static void Write(
        PlayerBuildLayout layout,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(graphicsBackends);
        var entries = new List<BuiltInResourceArchiveWriteEntry>
        {
            CaptureFile(
                PlayerPackagedResourceAddresses.RuntimeMetadata,
                layout.RuntimeMetadataStagingPath,
                "PlayerRuntimeMetadata"),
            CaptureFile(
                PlayerPackagedResourceAddresses.BuildTargetManifest,
                layout.BuildTargetManifestStagingPath,
                "PlayerBuildTargetManifest")
        };
        if (File.Exists(layout.SplashImageStagingPath))
            entries.Add(CaptureFile(
                PlayerPackagedResourceAddresses.SplashImage,
                layout.SplashImageStagingPath,
                "PlayerSplashImage"));

        CaptureCoreResources(entries, graphicsBackends, cancellationToken);
        BuiltInResourceArchive.Write(layout.PlayerResourceArchivePath, entries, cancellationToken);
    }

    private static BuiltInResourceArchiveWriteEntry CaptureFile(
        string address,
        string path,
        string assetType)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists)
            throw new FileNotFoundException(
                $"Packaged Player resource '{address}' was not staged.", file.FullName);
        using var stream = file.OpenRead();
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return BuiltInResourceArchiveWriteEntry.FromFile(
            address,
            file.FullName,
            assetType,
            sha256,
            file.Length,
            importer: "BEnginePlayerResource");
    }

    private static void CaptureCoreResources(
        ICollection<BuiltInResourceArchiveWriteEntry> entries,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends,
        CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveCoreRuntimeResources();
        foreach (var relativeDirectory in RuntimeCoreResourceDirectories)
        {
            var sourceDirectory = Path.Combine(sourceRoot, relativeDirectory);
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException(
                    $"BEngine runtime resource directory was not found: '{sourceDirectory}'.");
            foreach (var sourceFile in Directory.EnumerateFiles(
                         sourceDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsShaderResourceSupported(sourceFile, graphicsBackends)) continue;
                var relativeFile = Path.GetRelativePath(sourceRoot, sourceFile)
                    .Replace(Path.DirectorySeparatorChar, '/');
                entries.Add(CaptureFile(
                    PlayerPackagedResourceAddresses.CoreResourcesPrefix + relativeFile,
                    sourceFile,
                    "PlayerCoreResource"));
            }
        }
    }

    internal static bool IsShaderResourceSupported(
        string path,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends)
    {
        var fileName = Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".wgsl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".glsl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".spv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".msl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".metal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.Contains(".direct3d11.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.Direct3D11);
        if (fileName.Contains(".direct3d12.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.Direct3D12);
        if (fileName.Contains(".direct3d.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.Direct3D11) ||
                   graphicsBackends.Contains(GraphicsBackend.Direct3D12);
        if (fileName.Contains(".vulkan.", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".spirv.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.Vulkan);
        if (fileName.Contains(".opengles.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.OpenGLES);
        if (fileName.Contains(".opengl.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.OpenGL) ||
                   graphicsBackends.Contains(GraphicsBackend.OpenGLES) ||
                   graphicsBackends.Contains(GraphicsBackend.WebGL);
        if (fileName.Contains(".webgpu.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.WebGPU);
        if (fileName.Contains(".webgl.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.WebGL);
        if (fileName.Contains(".metal.", StringComparison.OrdinalIgnoreCase))
            return graphicsBackends.Contains(GraphicsBackend.Metal);
        return true;
    }

    private static string ResolveCoreRuntimeResources()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
             directory = directory.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "Resources"),
                         Path.Combine(directory.FullName, "src", "Core", "Resources")
                     })
                if (File.Exists(Path.Combine(candidate, "Icons", "BEngine.64.rgba")) &&
                    Directory.Exists(Path.Combine(candidate, "Shaders", "PortableScene")))
                    return candidate;
        }
        throw new DirectoryNotFoundException(
            "BEngine runtime Resources were not found. Re-export the engine with its Resources directory.");
    }
}
