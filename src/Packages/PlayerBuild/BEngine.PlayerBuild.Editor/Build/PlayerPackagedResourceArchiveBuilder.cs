using BEngine.Content;
using BEngine.Platform;
using BEngine.Rendering.Rhi;
using BEngine.ProjectSystem;
using System.Security.Cryptography;

namespace BEngine.Editor;

internal static class PlayerPackagedResourceArchiveBuilder
{
    private static readonly string[] RuntimeCoreResourceDirectories =
    [
        "Icons",
        "Shaders/PortableScene",
        "Shaders/Startup"
    ];

    internal static void Write(
        PlayerBuildLayout layout,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        CancellationToken cancellationToken,
        PlayerBuildContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(graphicsBackends);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        var entries = new List<BuiltInResourceArchiveWriteEntry>
        {
            CaptureFile(
                PlayerPackagedResourceAddresses.RuntimeMetadata,
                layout.RuntimeMetadataStagingPath,
                "PlayerRuntimeMetadata",
                fileSystem,
                pathService),
            CaptureFile(
                PlayerPackagedResourceAddresses.BuildTargetManifest,
                layout.BuildTargetManifestStagingPath,
                "PlayerBuildTargetManifest",
                fileSystem,
                pathService)
        };
        if (fileSystem.FileExists(layout.SplashImageStagingPath))
            entries.Add(CaptureFile(
                PlayerPackagedResourceAddresses.SplashImage,
                layout.SplashImageStagingPath,
                "PlayerSplashImage",
                fileSystem,
                pathService));

        CaptureCoreResources(
            entries, graphicsBackends, fileSystem, pathService, platformService, cancellationToken, context);
        using var engineCancellation = new BCancellationSource();
        using var registration = cancellationToken.Register(
            static state => _ = ((BCancellationSource)state!).Cancel(), engineCancellation);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            BuiltInResourceArchive.Write(
                layout.PlayerResourceArchivePath,
                entries,
                engineCancellation.Token,
                fileSystem,
                pathService,
                context?.ContentEncryptionKey,
                embedEncryptionKey: true,
                compress: context?.Request.CompressAssetBundles ?? true);
            if (context?.StrippingReport is { } report)
            {
                using var published = fileSystem.OpenRead(layout.PlayerResourceArchivePath);
                report.PlayerResourceArchiveBytes = published.Length;
                fileSystem.WriteAllText(context.StrippingReportPath!, YamlUtility.Serialize(report));
            }
        }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static BuiltInResourceArchiveWriteEntry CaptureFile(
        string address,
        string path,
        string assetType,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var fullPath = pathService.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException(
                $"Packaged Player resource '{address}' was not staged.", fullPath);
        return BuiltInResourceArchiveWriteEntry.FromMemory(
            address,
            fileSystem.ReadAllBytes(fullPath),
            assetType,
            importer: "BEnginePlayerResource");
    }

    private static void CaptureCoreResources(
        ICollection<BuiltInResourceArchiveWriteEntry> entries,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        CancellationToken cancellationToken,
        PlayerBuildContext? context)
    {
        var sourceRoot = ResolveCoreRuntimeResources(fileSystem, pathService, platformService);
        var resources = new List<RuntimePackageResourceInput>();
        foreach (var relativeDirectory in RuntimeCoreResourceDirectories)
        {
            var sourceDirectory = pathService.Combine(
                sourceRoot, relativeDirectory.Replace('/', pathService.DirectorySeparatorChar));
            if (!fileSystem.DirectoryExists(sourceDirectory))
                throw new DirectoryNotFoundException(
                    $"BEngine runtime resource directory was not found: '{sourceDirectory}'.");
            foreach (var sourceFile in fileSystem.EnumerateFiles(
                         sourceDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsShaderResourceSupported(sourceFile, graphicsBackends, pathService)) continue;
                var relativeFile = pathService.GetRelativePath(sourceRoot, sourceFile)
                    .Replace(pathService.DirectorySeparatorChar, '/');
                using var stream = fileSystem.OpenRead(sourceFile);
                resources.Add(new RuntimePackageResourceInput("core",
                    PlayerPackagedResourceAddresses.CoreResourcesPrefix + relativeFile,
                    sourceFile, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), fileSystem.GetFileLength(sourceFile)));
            }
        }
        if (context?.StrippingReport is { } report)
        {
            var workspace = ProjectWorkspace.Open(context.Request.ProjectPath, fileSystem, pathService);
            var filtered = PlayerResourceStripper.FilterPackageResources(new RuntimeManagedCodeReleaseInputSet([], resources, []), [],
                PlayerStrippingSettings.Load(workspace), report, coreResources: true,
                additionalAssemblyPaths: context.RuntimeManagedCodeAssemblyPaths.Concat(PlayerSerializationRoots.HostAssemblyPaths).Distinct(StringComparer.OrdinalIgnoreCase));
            resources = filtered.PackageResources.ToList();
            fileSystem.WriteAllText(context.StrippingReportPath!, YamlUtility.Serialize(report));
        }
        var compiled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            entries.Add(CaptureFile(resource.Address, resource.FilePath, "PlayerCoreResource", fileSystem, pathService));
            if (context?.Request.CompileShaders != true || !graphicsBackends.Contains(GraphicsBackend.Vulkan) ||
                !PlayerShaderBuildPipeline.TryReadSource(resource.FilePath, out var source)) continue;
            var address = "Assets/__BEngine/Compiled/Resources/" + GraphicsShaderArtifacts.GetResourcePath(source, GraphicsBackend.Vulkan);
            if (compiled.Add(address))
            {
                var bytes = PlayerShaderBuildPipeline.Compile(source);
                entries.Add(BuiltInResourceArchiveWriteEntry.FromMemory(address, bytes, "PlayerShaderArtifact"));
                if (context.StrippingReport is { } shaderReport) { shaderReport.OfflineShaderCount++; shaderReport.OfflineShaderBytes += bytes.Length; }
            }
        }
    }

    internal static bool IsShaderResourceSupported(
        string path,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends) =>
        IsShaderResourceSupported(path, graphicsBackends, PathUtility.Current);

    internal static bool IsShaderResourceSupported(
        string path,
        IReadOnlyCollection<GraphicsBackend> graphicsBackends,
        PathUtility pathService)
    {
        var fileName = pathService.GetFileName(path.Replace('/', pathService.DirectorySeparatorChar));
        var extension = pathService.GetExtension(fileName);
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

    private static string ResolveCoreRuntimeResources(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        foreach (var start in new[] { platformService.BaseDirectory, platformService.CurrentDirectory })
        for (var directory = pathService.GetFullPath(start); !string.IsNullOrWhiteSpace(directory);
             directory = pathService.GetDirectoryName(directory))
        {
            foreach (var candidate in new[]
                     {
                         pathService.Combine(directory, "Resources"),
                         pathService.Combine(directory, "src", "Core", "Resources"),
                         pathService.Combine(directory, "Engine", "src", "Core", "Resources")
                     })
                if (fileSystem.FileExists(pathService.Combine(candidate, "Icons", "BEngine.64.rgba")) &&
                    fileSystem.DirectoryExists(pathService.Combine(candidate, "Shaders", "PortableScene")))
                    return candidate;

            var parent = pathService.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(parent) || parent.Equals(directory, StringComparison.OrdinalIgnoreCase))
                break;
        }
        throw new DirectoryNotFoundException(
            "BEngine runtime Resources were not found. Re-export the engine with its Resources directory.");
    }
}
