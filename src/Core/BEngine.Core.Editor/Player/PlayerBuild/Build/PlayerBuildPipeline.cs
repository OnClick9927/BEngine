using BEngine.Hosting;
using System.Diagnostics;
using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor.Build;

public static class PlayerBuildPipeline
{
    public static IReadOnlyList<IPlayerBuildTargetProvider> registeredProviders =>
        EditorServiceAccess.GetService<IPlayerBuildPipeline>()?.RegisteredProviders ?? [];

    public static void RegisterProvider(IPlayerBuildTargetProvider provider, bool replace = false) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .RegisterProvider(provider, replace);

    public static bool UnregisterProvider(string providerId) =>
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .UnregisterProvider(providerId);

    public static bool CanBuild(string targetId, out string reason)
    {
        if (EditorServiceAccess.GetService<IPlayerBuildPipeline>() is { } pipeline)
            return pipeline.CanBuild(targetId, out reason);
        reason = "No Player content build provider is installed.";
        return false;
    }

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
        EditorServiceAccess.GetService<IPlayerBuildPipeline>()?.GetPrerequisites(targetId) ?? [];

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
        IPlayerBuildContentProvider contentProvider,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(contentProvider);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentNullException.ThrowIfNull(buildCapabilities);
        using var hostModules = PlayerBuildHostModules.Enter(contentProvider.RuntimeHostAnchors);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetId);
        if (!Enum.IsDefined(request.ManagedStripping))
            throw new ArgumentOutOfRangeException(nameof(request.ManagedStripping));
        PlayerGarbageCollectionBuildSettings.ValidateWindows(request.WindowsGarbageCollection, request.WindowsGcHeapCount);
        if (request.ManagedStripping == PlayerManagedStrippingLevel.Full && !request.SelfContained)
            throw new InvalidDataException("Full Player stripping requires a self-contained build.");
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(
            pathService.GetFullPath(request.ProjectPath), fileSystem, pathService);
        var projectPath = workspace.RootPath;
        var output = pathService.GetFullPath(request.OutputDirectory);
        PlayerBuildLayout.ValidateOutputLocation(projectPath, output, fileSystem, pathService);
        ValidateLowercaseOutputDirectory(output, pathService);
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
        if (target.Platform == BuildTargetPlatform.Web && !Enum.IsDefined(request.WebScriptCompilation))
            throw new ArgumentOutOfRangeException(nameof(request.WebScriptCompilation));
        if (target.Platform == BuildTargetPlatform.Web && !Enum.IsDefined(request.WebFontBackend))
            throw new ArgumentOutOfRangeException(nameof(request.WebFontBackend));
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
            WebScriptCompilation = request.WebScriptCompilation,
            WebFontBackend = request.WebFontBackend,
            IncludeDebugSymbols = request.IncludeDebugSymbols,
            EncryptContent = request.EncryptContent,
            EncryptionKey = request.EncryptionKey,
            ReplaceExisting = request.ReplaceExisting,
            BuildVersion = request.BuildVersion,
            HotResourceVersion = PlayerBuildSettingsStore.NormalizeHotResourceVersion(
                request.HotResourceVersion),
            EnableHotUpdate = request.EnableHotUpdate,
            ContentUpdatePolicy = request.ContentUpdatePolicy,
            ContentOptions = new Dictionary<string, string>(request.ContentOptions, StringComparer.Ordinal),
            ManagedStripping = request.ManagedStripping,
            WritePlayerLog = request.WritePlayerLog,
            SplashScreenEnabled = true,
            SplashImage = request.SplashImage,
            SplashBackgroundColor = request.SplashBackgroundColor,
            SplashMinimumDurationSeconds = 0.5f,
            CacheDirectory = HostDeploymentManifest.NormalizeCacheDirectory(request.CacheDirectory),
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
        var context = new PlayerBuildContext(normalized, target, staging, progress)
        {
            PackagingRequirements = contentProvider.PackagingRequirements ??
                throw new InvalidOperationException("The content provider returned null packaging requirements.")
        };
        var timer = Stopwatch.StartNew();
        using var buildSession = EditorBuildEvents.BeginBuild(output);
        var succeeded = false;
        try
        {
            fileSystem.CreateDirectory(staging);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Preparing,
                $"Preparing {target.TargetId}", 0));
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                "Building built-in AOT resources", 0.1f));
            await contentProvider.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            var layout = PlayerBuildLayout.Create(workspace, staging, fileSystem, pathService);
            PlayerBuildStaging.Prepare(workspace, layout, context, fileSystem, pathService, cancellationToken);
            await contentProvider.FinalizeAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PlayerBuildStaging.Complete(layout, fileSystem);
            await provider.BuildAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Browser and native-mobile SDK artifacts contain case-sensitive managed/runtime
            // names. The engine-owned content tree remains portable and lowercase.
            PlayerOutputValidation.ValidateLowercaseTree(
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
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Completed, output, 1));
            var result = new PlayerBuildResult(target, output, context.ContentVersion, timer.Elapsed)
            {
                StrippingReportPath = context.StrippingReportPath,
                HostContractPath = context.HostContractPath,
                ContentEncryptionKeyFingerprint = context.ContentEncryptionKey?.Fingerprint
            };
            succeeded = true;
            return result;
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
            // If cleanup above throws, disposal still reports a failed build.
            buildSession.Complete(succeeded);
        }
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
