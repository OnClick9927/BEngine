using BEngine.Hosting;
using BEngine.Content;
using BEngine.Platform;
using BEngine.Rendering.Rhi;
using BEngine.ProjectSystem;
using BEngine.Build;
using System.Security.Cryptography;

namespace BEngine.Editor.Build;

internal static class PlayerPackagedResourceArchiveBuilder
{
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
                HostPackagedResourceAddresses.RuntimeMetadata,
                layout.RuntimeMetadataStagingPath,
                "PlayerRuntimeMetadata",
                fileSystem,
                pathService),
            CaptureBuildTargetManifest(
                layout.BuildTargetManifestStagingPath,
                fileSystem,
                pathService,
                cancellationToken)
        };
        if (fileSystem.FileExists(layout.SplashImageStagingPath))
            entries.Add(CaptureFile(
                HostPackagedResourceAddresses.SplashImage,
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
                (BEngine.Content.ContentEncryptionKey?)context?.ContentEncryptionKey,
                embedEncryptionKey: true,
                compress: PlatformContentBuildPolicy.CompressBuiltInArchive(
                    context?.Target.Platform, AssetBundlePlayerBuildOptions.GetCompression(context?.Request.ContentOptions)));
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

    private static BuiltInResourceArchiveWriteEntry CaptureBuildTargetManifest(
        string path,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        var fullPath = pathService.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("The packaged Player build-target manifest was not staged.", fullPath);
        var yaml = fileSystem.ReadAllBytes(fullPath);
        _ = BuildTargetManifestSerializer.Deserialize(yaml);
        // The editable/staged manifest stays YAML. Only this immutable runtime archive
        // entry uses compact data, so startup does not initialize the YAML codecs.
        return BuiltInResourceArchiveWriteEntry.FromMemory(
            HostPackagedResourceAddresses.BuildTargetManifest,
            BinaryDataSerializer.CompileYaml(yaml, cancellationToken),
            "PlayerBuildTargetManifest",
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
        var resources = new List<RuntimePackageResourceInput>();
        var packages = BPackageCatalog.DiscoverAvailable().packages;
        foreach (var resource in RuntimeBuildResources.EnumerateFiles(assembly =>
        {
            var definition = packages.FirstOrDefault(package => package.Document.Runtime?.Assembly == assembly)
                ?? throw new InvalidDataException($"Runtime resource owner '{assembly}' is unavailable.");
            return pathService.Combine(pathService.GetDirectoryName(definition.Path)!, "Resources");
        }, fileSystem, pathService, cancellationToken))
        {
            if (!IsShaderResourceSupported(resource.FullPath, graphicsBackends, pathService)) continue;
            using var stream = fileSystem.OpenRead(resource.FullPath);
            resources.Add(new RuntimePackageResourceInput("core",
                HostPackagedResourceAddresses.CoreResourcesPrefix + resource.RelativePath,
                resource.FullPath, Convert.ToHexStringLower(SHA256.HashData(stream)), fileSystem.GetFileLength(resource.FullPath)));
        }
        if (context?.StrippingReport is { } report)
        {
            resources = PlayerResourceBuildPipeline.SelectHostResources(context, resources,
                static resource => ShaderBuildPipeline.IsRequiredBootstrapResource(resource.Address), cancellationToken).ToList();
            fileSystem.WriteAllText(context.StrippingReportPath!, YamlUtility.Serialize(report));
        }
        var compiled = new HashSet<string>(StringComparer.Ordinal);
        var offlineBackends = PlatformGraphicsBuildPolicy.GetOfflineBackends(
            graphicsBackends, context?.Request.CompileShaders == true);
        foreach (var resource in resources)
        {
            entries.Add(CaptureFile(resource.Address, resource.FilePath, "PlayerCoreResource", fileSystem, pathService));
            foreach (var backend in offlineBackends)
            {
                if (!ShaderBuildPipeline.TryReadSource(resource.FilePath, backend, out var source)) continue;
                var address = "Assets/__BEngine/Compiled/Resources/" + source.GetResourcePath(backend);
                if (!compiled.Add(address)) continue;
                var bytes = ShaderBuildPipeline.Compile(source, backend, cancellationToken);
                entries.Add(BuiltInResourceArchiveWriteEntry.FromMemory(address, bytes, "PlayerShaderArtifact"));
                if (context?.StrippingReport is { } shaderReport) { shaderReport.OfflineShaderCount++; shaderReport.OfflineShaderBytes += bytes.Length; }
            }
        }
    }

    internal static bool IsShaderResourceSupported(
        string path, IReadOnlyCollection<GraphicsBackend> graphicsBackends) =>
        ShaderBuildPipeline.IsResourceSupported(path, graphicsBackends);

    internal static bool IsShaderResourceSupported(
        string path, IReadOnlyCollection<GraphicsBackend> graphicsBackends, PathUtility pathService) =>
        ShaderBuildPipeline.IsResourceSupported(path, graphicsBackends);
}
