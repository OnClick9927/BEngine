using System.Diagnostics;
using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class PlayerBuildPipeline
{
    public static IReadOnlyList<IPlayerBuildTargetProvider> registeredProviders =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>().RegisteredProviders;

    public static void RegisterProvider(IPlayerBuildTargetProvider provider, bool replace = false) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .RegisterProvider(provider, replace);

    public static bool UnregisterProvider(string providerId) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .UnregisterProvider(providerId);

    public static bool CanBuild(string targetId, out string reason) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .CanBuild(targetId, out reason);

    internal static bool CanBuildCore(
        string targetId,
        IReadOnlyList<IPlayerBuildTargetProvider> providers,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(providers);
        BuildTargetDescriptor target;
        try { target = BEngine.Platform.BuildTargetUtility.Current.Get(targetId); }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
        var reasons = new List<string>();
        foreach (var provider in providers)
        {
            if (provider is IPlayerBuildPrerequisiteProvider prerequisiteProvider &&
                !prerequisiteProvider.SupportsTarget(target)) continue;
            if (provider.CanBuild(target, out var providerReason))
            {
                reason = string.Empty;
                return true;
            }
            if (!string.IsNullOrWhiteSpace(providerReason))
                reasons.Add($"{provider.ProviderId}: {providerReason}");
        }
        reason = reasons.Count == 0
            ? $"No build provider recognizes '{target.TargetId}'."
            : string.Join("; ", reasons);
        return false;
    }

    public static IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(string targetId) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .GetPrerequisites(targetId);

    internal static IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisitesCore(
        string targetId,
        IReadOnlyList<IPlayerBuildTargetProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var target = BEngine.Platform.BuildTargetUtility.Current.Get(targetId);
        return providers
            .OfType<IPlayerBuildPrerequisiteProvider>()
            .Where(provider => provider.SupportsTarget(target))
            .SelectMany(provider => provider.GetPrerequisites(target))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public static Task<PlayerBuildResult> BuildAsync(
        PlayerBuildRequest request,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .BuildAsync(request, progress, cancellationToken);

    internal static async Task<PlayerBuildResult> BuildCoreAsync(
        PlayerBuildRequest request,
        IReadOnlyList<IPlayerBuildTargetProvider> providers,
        IAssetBundleBuildPipeline assetBundleBuildPipeline,
        IProjectAssetDatabaseFactory assetDatabaseFactory,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(assetBundleBuildPipeline);
        ArgumentNullException.ThrowIfNull(assetDatabaseFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentNullException.ThrowIfNull(buildCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetId);
        if (!Enum.IsDefined(request.ManagedStripping))
            throw new ArgumentOutOfRangeException(nameof(request.ManagedStripping));
        PlayerGarbageCollectionBuildSettings.ValidateWindows(request.WindowsGarbageCollection, request.WindowsGcHeapCount);
        if (request.ManagedStripping == PlayerManagedStrippingLevel.Full && !request.SelfContained)
            throw new InvalidDataException("Full Player stripping requires a self-contained build.");
        var projectPath = pathService.GetFullPath(request.ProjectPath);
        var output = pathService.GetFullPath(request.OutputDirectory);
        PlayerBuildLayout.ValidateOutputLocation(projectPath, output, fileSystem, pathService);
        ValidateLowercaseOutputDirectory(output, pathService);
        var workspace = ProjectSystem.ProjectWorkspace.Open(projectPath, fileSystem, pathService);
        if (fileSystem.FileExists(output))
            throw new IOException($"Player build output is a file: '{output}'.");
        if (fileSystem.DirectoryExists(output) && !request.ReplaceExisting)
            throw new IOException($"Player build output already exists: '{output}'.");
        var parent = pathService.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Player build output '{output}' has no parent directory.");
        fileSystem.CreateDirectory(parent);
        var staging = pathService.Combine(
            parent, $".{pathService.GetFileName(output)}.{Guid.NewGuid():N}.staging");
        EnsureDirectChild(parent, staging, pathService);
        var target = BEngine.Platform.BuildTargetUtility.Current.Get(request.TargetId);
        if (request.EncryptContent && target.Platform == BuildTargetPlatform.Web)
            throw new NotSupportedException("Content encryption currently requires a native Player; browser AES-GCM content loading is not supported.");
        var provider = ResolveProvider(target, providers);
        var normalized = new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = target.TargetId,
            GraphicsBackends = PlayerGraphicsBuildSettings.Resolve(request, target, buildCapabilities),
            CompileShaders = request.CompileShaders,
            RuntimeShaderCompilation = request.RuntimeShaderCompilation,
            HostContractPath = request.HostContractPath,
            Configuration = request.Configuration,
            DevelopmentBuild = request.DevelopmentBuild,
            SelfContained = request.SelfContained,
            WindowsGarbageCollection = request.WindowsGarbageCollection,
            WindowsGcHeapCount = request.WindowsGcHeapCount,
            IncludeDebugSymbols = request.IncludeDebugSymbols,
            EncryptContent = request.EncryptContent,
            EncryptionKey = request.EncryptionKey,
            ReplaceExisting = request.ReplaceExisting,
            BuildVersion = request.BuildVersion,
            HotResourceVersion = PlayerBuildSettingsStore.NormalizeHotResourceVersion(
                request.HotResourceVersion),
            EnableHotUpdate = request.EnableHotUpdate,
            ContentUpdatePolicy = request.ContentUpdatePolicy,
            CompressAssetBundles = request.CompressAssetBundles,
            ManagedStripping = request.ManagedStripping,
            WritePlayerLog = request.WritePlayerLog,
            SplashScreenEnabled = request.SplashScreenEnabled,
            SplashImage = request.SplashImage,
            SplashBackgroundColor = request.SplashBackgroundColor,
            SplashMinimumDurationSeconds = request.SplashMinimumDurationSeconds,
            CacheDirectory = PlayerBootstrapManifest.NormalizeCacheDirectory(request.CacheDirectory),
            AndroidApplicationIdentifier = PlayerBuildSettingsStore.NormalizeApplicationIdentifier(
                request.AndroidApplicationIdentifier),
            AndroidMinimumApiLevel = PlayerBuildSettingsStore.NormalizeAndroidMinimumApiLevel(
                request.AndroidMinimumApiLevel),
            AndroidBuildAppBundle = request.AndroidBuildAppBundle,
            IosBundleIdentifier = PlayerBuildSettingsStore.NormalizeApplicationIdentifier(
                request.IosBundleIdentifier),
            IosMinimumVersion = PlayerBuildSettingsStore.NormalizeIosMinimumVersion(
                request.IosMinimumVersion),
            IosCodesignKey = PlayerBuildSettingsStore.NormalizeIosSigningValue(
                request.IosCodesignKey, "iOS signing certificate"),
            IosProvisioningProfile = PlayerBuildSettingsStore.NormalizeIosSigningValue(
                request.IosProvisioningProfile, "iOS provisioning profile"),
            HotUpdateStartupScene = PlayerBuildSettingsStore.NormalizeStartupScene(
                request.HotUpdateStartupScene, workspace, fileSystem, pathService),
            Scenes = request.Scenes?.ToArray() ?? throw new ArgumentNullException(nameof(request.Scenes)),
            PlayerHostProjectPath = request.PlayerHostProjectPath,
            AdditionalMsBuildProperties = new Dictionary<string, string>(
                request.AdditionalMsBuildProperties ??
                throw new ArgumentNullException(nameof(request.AdditionalMsBuildProperties)),
                StringComparer.Ordinal)
        };
        var context = new PlayerBuildContext(normalized, target, staging, progress);
        context.ContentEncryptionKey = normalized.EncryptContent
            ? BEngine.Content.ContentEncryptionKey.FromPassphrase(normalized.EncryptionKey)
            : null;
        var timer = Stopwatch.StartNew();
        BuildPipeline.RaiseBuildStarted(output);
        var succeeded = false;
        try
        {
            fileSystem.CreateDirectory(staging);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Preparing,
                $"Preparing {target.TargetId}", 0));
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                "Building built-in AOT resources", 0.1f));
            context.ContentVersion = await PlayerContentBuildPipeline.BuildAsync(
                    context, assetBundleBuildPipeline, assetDatabaseFactory, fileSystem, pathService,
                    buildCapabilities, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var graphicsBackends = normalized.GraphicsBackends;
            var layout = PlayerBuildLayout.Create(workspace, staging, fileSystem, pathService);
            PlayerStreamingAssetsBuilder.Copy(
                workspace, layout, fileSystem, pathService, cancellationToken);
            BuildTargetManifestSerializer.Save(
                BuildTargetManifest.FromDescriptor(target, graphicsBackends),
                layout.PlayerResourceStagingDirectory,
                fileSystem,
                pathService);
            var runtimeMetadataPath = layout.RuntimeMetadataStagingPath;
            var runtimeMetadata = ProjectSystem.RuntimeMetadataSerializer.Load(
                runtimeMetadataPath, fileSystem, pathService);
            var splashImageResource = PrepareSplashImage(
                workspace, layout, normalized, fileSystem, pathService, platformService);
            runtimeMetadata.PlayerBootstrap = new PlayerBootstrapManifest
            {
                ProductName = workspace.Project.Name,
                BuildVersion = normalized.BuildVersion,
                DevelopmentBuild = normalized.DevelopmentBuild,
                WritePlayerLog = normalized.WritePlayerLog,
                Executable = target.Platform == BuildTargetPlatform.Windows
                    ? $"{layout.PlayerName}.exe"
                    : layout.PlayerName,
                DataDirectory = layout.DataDirectoryName,
                ResourceDirectory = layout.DataDirectoryName,
                AssemblyDirectory = $"{layout.DataDirectoryName}/" +
                                    PlayerPackagedResourceAddresses.AssemblyDirectoryName,
                PlayerResourceArchive = $"{layout.DataDirectoryName}/" +
                                        PlayerPackagedResourceAddresses.ResourcesDirectoryName + "/" +
                                        PlayerPackagedResourceAddresses.PlayerArchiveFileName,
                BuildTargetManifest = string.Empty,
                BuildTargetResource = PlayerPackagedResourceAddresses.BuildTargetManifest,
                SplashScreenEnabled = normalized.SplashScreenEnabled,
                SplashImage = string.Empty,
                SplashImageResource = splashImageResource,
                SplashBackgroundColor = normalized.SplashBackgroundColor,
                SplashMinimumDurationSeconds = normalized.SplashMinimumDurationSeconds,
                CacheDirectory = normalized.CacheDirectory,
                HotUpdateStartupScene = normalized.HotUpdateStartupScene
            };
            ProjectSystem.RuntimeMetadataSerializer.Save(
                runtimeMetadata, runtimeMetadataPath, fileSystem, pathService);
            PlayerPackagedResourceArchiveBuilder.Write(
                layout, graphicsBackends, fileSystem, pathService, platformService, cancellationToken, context);
            DeleteStagedPlayerResources(layout, fileSystem);
            await provider.BuildAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Browser and native-mobile SDK artifacts contain case-sensitive managed/runtime
            // names. The engine-owned content tree remains portable and lowercase.
            PlayerBuildLayout.ValidateLowercaseOutputTree(
                target.Platform is BuildTargetPlatform.Web or BuildTargetPlatform.Android or BuildTargetPlatform.IOS
                    ? layout.DataDirectory
                    : staging,
                fileSystem,
                pathService);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Publishing,
                $"Publishing {target.TargetId}", 0.95f));
            PlayerBuildLayout.ValidateOutputLocation(projectPath, output, fileSystem, pathService);
            PublishStaging(
                staging, output, parent, normalized.ReplaceExisting, fileSystem, pathService);
            succeeded = true;
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Completed, output, 1));
            return new PlayerBuildResult(target, output, context.ContentVersion, timer.Elapsed)
            {
                StrippingReportPath = context.StrippingReportPath,
                HostContractPath = context.HostContractPath,
                ContentEncryptionKeyFingerprint = context.ContentEncryptionKey?.Fingerprint
            };
        }
        finally
        {
            if (succeeded && context.StrippingWorkDirectory is { } strippingWork && fileSystem.DirectoryExists(strippingWork))
            {
                var strippingParent = pathService.Combine(workspace.RootPath, "Temp", "PlayerStripping");
                EnsureDirectChild(strippingParent, strippingWork, pathService);
                fileSystem.DeleteDirectory(strippingWork, recursive: true);
            }
            if (fileSystem.DirectoryExists(staging))
            {
                EnsureDirectChild(parent, staging, pathService);
                fileSystem.DeleteDirectory(staging, recursive: true);
            }
            BuildPipeline.RaiseBuildFinished(output, succeeded);
        }
    }

    private static string PrepareSplashImage(
        ProjectSystem.ProjectWorkspace workspace,
        PlayerBuildLayout layout,
        PlayerBuildRequest request,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        if (!request.SplashScreenEnabled) return string.Empty;
        var source = string.IsNullOrWhiteSpace(request.SplashImage)
            ? ResolveDefaultSplashImage(fileSystem, pathService, platformService)
            : workspace.ResolveInside(request.SplashImage.Replace('/', pathService.DirectorySeparatorChar));
        if (!fileSystem.FileExists(source))
            throw new FileNotFoundException("The configured Player splash image was not found.", source);
        if (!pathService.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Player splash image must be a PNG file.");
        var bytes = fileSystem.ReadAllBytes(source);
        if (!BEngine.Rendering.PngImageCodec.TryDecode(bytes, out var width, out var height, out _) ||
            width > 8192 || height > 8192)
            throw new InvalidDataException(
                $"The Player splash image is not a supported PNG or exceeds 8192x8192: '{source}'.");
        fileSystem.WriteAllBytes(layout.SplashImageStagingPath, bytes);
        return PlayerPackagedResourceAddresses.SplashImage;
    }

    private static void DeleteStagedPlayerResources(PlayerBuildLayout layout, FileUtility fileSystem)
    {
        foreach (var path in new[]
                 {
                     layout.RuntimeMetadataStagingPath,
                     layout.BuildTargetManifestStagingPath,
                     layout.SplashImageStagingPath
                 })
            if (fileSystem.FileExists(path)) fileSystem.DeleteFile(path);
    }

    private static string ResolveDefaultSplashImage(
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
                         pathService.Combine(directory, "Editor", "Icons", "BEngine.png"),
                         pathService.Combine(
                             directory, "src", "Core", "Editor", "Icons", "BEngine.png"),
                         pathService.Combine(
                             directory, "Engine", "src", "Core", "Editor", "Icons", "BEngine.png")
                     })
                if (fileSystem.FileExists(candidate)) return candidate;

            var parent = pathService.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(parent) || parent.Equals(directory, StringComparison.OrdinalIgnoreCase))
                break;
        }
        throw new FileNotFoundException(
            "The default BEngine splash image was not found. Re-export the engine or configure an Assets/*.png image.");
    }

    private static void PublishStaging(
        string staging,
        string output,
        string parent,
        bool replaceExisting,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        if (fileSystem.FileExists(output))
            throw new IOException($"Player build output became a file while building: '{output}'.");
        if (!fileSystem.DirectoryExists(output))
        {
            fileSystem.MoveDirectory(staging, output);
            return;
        }
        if (!replaceExisting)
            throw new IOException($"Player build output was created while building: '{output}'.");

        var backup = pathService.Combine(
            parent, $".{pathService.GetFileName(output)}.{Guid.NewGuid():N}.backup");
        EnsureDirectChild(parent, backup, pathService);
        fileSystem.MoveDirectory(output, backup);
        try { fileSystem.MoveDirectory(staging, output); }
        catch
        {
            if (!fileSystem.FileExists(output) && !fileSystem.DirectoryExists(output) &&
                fileSystem.DirectoryExists(backup))
                fileSystem.MoveDirectory(backup, output);
            throw;
        }

        try { fileSystem.DeleteDirectory(backup, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"The previous Player build remains at '{backup}': {exception.Message}");
        }
    }

    private static IPlayerBuildTargetProvider ResolveProvider(
        BuildTargetDescriptor target,
        IReadOnlyList<IPlayerBuildTargetProvider> providers)
    {
        var reasons = new List<string>();
        foreach (var provider in providers)
        {
            if (provider is IPlayerBuildPrerequisiteProvider prerequisiteProvider &&
                !prerequisiteProvider.SupportsTarget(target)) continue;
            if (provider.CanBuild(target, out var reason)) return provider;
            if (!string.IsNullOrWhiteSpace(reason)) reasons.Add($"{provider.ProviderId}: {reason}");
        }
        throw new PlatformNotSupportedException(
            $"No Player build provider can build '{target.TargetId}'. {string.Join("; ", reasons)}");
    }

    private static void EnsureDirectChild(string parent, string path, PathUtility pathService)
    {
        var fullParent = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(parent));
        var fullPath = pathService.GetFullPath(path);
        if (!pathService.GetDirectoryName(fullPath)!.Equals(fullParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build staging path escapes '{fullParent}': '{fullPath}'.");
    }

    private static void ValidateLowercaseOutputDirectory(string output, PathUtility pathService)
    {
        var name = pathService.GetFileName(pathService.TrimEndingDirectorySeparator(output));
        if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Player build output directory names must be lowercase: '{name}'.");
    }
}
