using System.Diagnostics;
using BEngine.Build;
using BEngine.Content;

namespace BEngine.Editor;

public static class PlayerBuildPipeline
{
    private static readonly object Gate = new();
    private static readonly List<IPlayerBuildTargetProvider> Providers =
    [
        new DotNetDesktopBuildTargetProvider(),
        new DotNetAndroidBuildTargetProvider(),
        new DotNetIosBuildTargetProvider(),
        new DotNetWebAssemblyBuildTargetProvider()
    ];

    public static IReadOnlyList<IPlayerBuildTargetProvider> registeredProviders
    {
        get { lock (Gate) return Providers.ToArray(); }
    }

    public static void RegisterProvider(IPlayerBuildTargetProvider provider, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (Gate)
        {
            var existing = Providers.FindIndex(item =>
                item.ProviderId.Equals(provider.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && !replace)
                throw new InvalidOperationException($"Build provider '{provider.ProviderId}' is already registered.");
            if (existing >= 0) Providers[existing] = provider;
            else Providers.Add(provider);
        }
    }

    public static bool UnregisterProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        lock (Gate)
            return Providers.RemoveAll(item =>
                item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)) != 0;
    }

    public static bool CanBuild(string targetId, out string reason)
    {
        BuildTargetDescriptor target;
        try { target = BuildTargetCatalog.Get(targetId); }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
        lock (Gate)
        {
            var reasons = new List<string>();
            foreach (var provider in Providers)
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
    }

    public static IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(string targetId)
    {
        var target = BuildTargetCatalog.Get(targetId);
        lock (Gate)
        {
            return Providers
                .OfType<IPlayerBuildPrerequisiteProvider>()
                .Where(provider => provider.SupportsTarget(target))
                .SelectMany(provider => provider.GetPrerequisites(target))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }
    }

    public static async Task<PlayerBuildResult> BuildAsync(
        PlayerBuildRequest request,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetId);
        var projectPath = Path.GetFullPath(request.ProjectPath);
        var output = Path.GetFullPath(request.OutputDirectory);
        ValidateLowercaseOutputDirectory(output);
        var workspace = ProjectSystem.ProjectWorkspace.Open(projectPath);
        if (File.Exists(output))
            throw new IOException($"Player build output is a file: '{output}'.");
        if (Directory.Exists(output) && !request.ReplaceExisting)
            throw new IOException($"Player build output already exists: '{output}'.");
        var parent = Path.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Player build output '{output}' has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.staging");
        EnsureDirectChild(parent, staging);
        var target = BuildTargetCatalog.Get(request.TargetId);
        var provider = ResolveProvider(target);
        var normalized = new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = target.TargetId,
            Configuration = request.Configuration,
            DevelopmentBuild = request.DevelopmentBuild,
            SelfContained = request.SelfContained,
            IncludeDebugSymbols = request.IncludeDebugSymbols,
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
            Scenes = request.Scenes?.ToArray() ?? throw new ArgumentNullException(nameof(request.Scenes)),
            PlayerHostProjectPath = request.PlayerHostProjectPath,
            AdditionalMsBuildProperties = new Dictionary<string, string>(
                request.AdditionalMsBuildProperties ??
                throw new ArgumentNullException(nameof(request.AdditionalMsBuildProperties)),
                StringComparer.Ordinal)
        };
        var context = new PlayerBuildContext(normalized, target, staging, progress);
        var timer = Stopwatch.StartNew();
        BuildPipeline.RaiseBuildStarted(output);
        var succeeded = false;
        try
        {
            Directory.CreateDirectory(staging);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Preparing,
                $"Preparing {target.TargetId}", 0));
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                "Building built-in AOT resources", 0.1f));
            context.ContentVersion = await PlayerContentBuildPipeline.BuildAsync(context, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var graphicsBackends = PlatformPlayerBuildCapabilities.TryGetRenderer(target.TargetId, out var renderer)
                ? renderer.Backends
                : target.GraphicsBackends;
            var layout = PlayerBuildLayout.Create(workspace, staging);
            BuildTargetManifestSerializer.Save(
                BuildTargetManifest.FromDescriptor(target, graphicsBackends),
                layout.PlayerResourceStagingDirectory);
            var runtimeMetadataPath = layout.RuntimeMetadataStagingPath;
            var runtimeMetadata = ProjectSystem.RuntimeMetadataSerializer.Load(runtimeMetadataPath);
            var splashImageResource = PrepareSplashImage(workspace, layout, normalized);
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
                HotUpdateStartupScene = workspace.Project.StartupScene
            };
            ProjectSystem.RuntimeMetadataSerializer.Save(runtimeMetadata, runtimeMetadataPath);
            PlayerPackagedResourceArchiveBuilder.Write(layout, graphicsBackends, cancellationToken);
            DeleteStagedPlayerResources(layout);
            await provider.BuildAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PlayerBuildLayout.ValidateLowercaseOutputTree(staging);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Publishing,
                $"Publishing {target.TargetId}", 0.95f));
            PublishStaging(staging, output, parent, normalized.ReplaceExisting);
            succeeded = true;
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Completed, output, 1));
            return new PlayerBuildResult(target, output, context.ContentVersion, timer.Elapsed);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                EnsureDirectChild(parent, staging);
                Directory.Delete(staging, recursive: true);
            }
            BuildPipeline.RaiseBuildFinished(output, succeeded);
        }
    }

    private static string PrepareSplashImage(
        ProjectSystem.ProjectWorkspace workspace,
        PlayerBuildLayout layout,
        PlayerBuildRequest request)
    {
        if (!request.SplashScreenEnabled) return string.Empty;
        var source = string.IsNullOrWhiteSpace(request.SplashImage)
            ? ResolveDefaultSplashImage()
            : workspace.ResolveInside(request.SplashImage.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(source))
            throw new FileNotFoundException("The configured Player splash image was not found.", source);
        if (!Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Player splash image must be a PNG file.");
        var bytes = File.ReadAllBytes(source);
        if (!BEngine.Rendering.PngImageCodec.TryDecode(bytes, out var width, out var height, out _) ||
            width > 8192 || height > 8192)
            throw new InvalidDataException(
                $"The Player splash image is not a supported PNG or exceeds 8192x8192: '{source}'.");
        File.WriteAllBytes(layout.SplashImageStagingPath, bytes);
        return PlayerPackagedResourceAddresses.SplashImage;
    }

    private static void DeleteStagedPlayerResources(PlayerBuildLayout layout)
    {
        foreach (var path in new[]
                 {
                     layout.RuntimeMetadataStagingPath,
                     layout.BuildTargetManifestStagingPath,
                     layout.SplashImageStagingPath
                 })
            if (File.Exists(path)) File.Delete(path);
    }

    private static string ResolveDefaultSplashImage()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
             directory = directory.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "Editor", "Icons", "BEngine.png"),
                         Path.Combine(directory.FullName, "src", "Core", "Editor", "Icons", "BEngine.png")
                     })
                if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(
            "The default BEngine splash image was not found. Re-export the engine or configure an Assets/*.png image.");
    }

    private static void PublishStaging(
        string staging,
        string output,
        string parent,
        bool replaceExisting)
    {
        if (File.Exists(output))
            throw new IOException($"Player build output became a file while building: '{output}'.");
        if (!Directory.Exists(output))
        {
            Directory.Move(staging, output);
            return;
        }
        if (!replaceExisting)
            throw new IOException($"Player build output was created while building: '{output}'.");

        var backup = Path.Combine(parent, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.backup");
        EnsureDirectChild(parent, backup);
        Directory.Move(output, backup);
        try { Directory.Move(staging, output); }
        catch
        {
            if (!File.Exists(output) && !Directory.Exists(output) && Directory.Exists(backup))
                Directory.Move(backup, output);
            throw;
        }

        try { Directory.Delete(backup, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"The previous Player build remains at '{backup}': {exception.Message}");
        }
    }

    private static IPlayerBuildTargetProvider ResolveProvider(BuildTargetDescriptor target)
    {
        lock (Gate)
        {
            var reasons = new List<string>();
            foreach (var provider in Providers)
            {
                if (provider is IPlayerBuildPrerequisiteProvider prerequisiteProvider &&
                    !prerequisiteProvider.SupportsTarget(target)) continue;
                if (provider.CanBuild(target, out var reason)) return provider;
                if (!string.IsNullOrWhiteSpace(reason)) reasons.Add($"{provider.ProviderId}: {reason}");
            }
            throw new PlatformNotSupportedException(
                $"No Player build provider can build '{target.TargetId}'. {string.Join("; ", reasons)}");
        }
    }

    private static void EnsureDirectChild(string parent, string path)
    {
        var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetDirectoryName(fullPath)!.Equals(fullParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build staging path escapes '{fullParent}': '{fullPath}'.");
    }

    private static void ValidateLowercaseOutputDirectory(string output)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(output));
        if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Player build output directory names must be lowercase: '{name}'.");
    }
}
