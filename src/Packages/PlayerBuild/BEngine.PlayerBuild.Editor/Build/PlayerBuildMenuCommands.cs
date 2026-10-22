using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem;
using System.Text.Json;

namespace BEngine.Editor;

internal static class PlayerBuildMenuCommands
{
    private static CancellationTokenSource? _activeBuild;

    internal static event Action? stateChanged;
    internal static bool IsBuilding => _activeBuild is not null;

    [MenuItem("File/Build Settings...", false, 220)]
    private static void OpenBuildSettings() => PlayerBuildSettingsWindow.Open();

    [MenuItem("File/Build Settings...", true)]
    private static bool ValidateBuildSettings() =>
        !string.IsNullOrWhiteSpace(EditorApplication.projectPath);

    [MenuItem("File/Build And Run", false, 221)]
    private static void BuildAndRunFromMenu()
    {
        if (IsBuilding || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return;
        try
        {
            var settings = PlayerBuildSettingsStore.Load(EditorApplication.projectPath);
            ChooseOutput(settings, runAfterBuild: true);
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    [MenuItem("File/Build And Run", true)]
    private static bool ValidateBuildAndRun()
    {
        if (IsBuilding || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return false;
        try
        {
            var settings = PlayerBuildSettingsStore.Load(EditorApplication.projectPath);
            return CanRunLocally(settings.TargetId) &&
                   PlayerBuildSettingsStore.GetEnabledScenes(settings).Count > 0;
        }
        catch { return false; }
    }

    [MenuItem("File/Build Content Update", false, 222)]
    private static void BuildContentUpdateFromMenu()
    {
        if (IsBuilding || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return;
        try
        {
            var settings = PlayerBuildSettingsStore.Load(EditorApplication.projectPath);
            ChooseContentUpdateOutput(settings);
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    [MenuItem("File/Build Content Update", true)]
    private static bool ValidateBuildContentUpdate()
    {
        if (IsBuilding || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return false;
        try
        {
            return PlayerBuildSettingsStore.GetEnabledScenes(
                PlayerBuildSettingsStore.Load(EditorApplication.projectPath)).Count > 0;
        }
        catch { return false; }
    }

    internal static void ChooseOutput(PlayerBuildSettings settings, bool runAfterBuild)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (IsBuilding || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return;
        PlayerBuildSettingsStore.Save(EditorApplication.projectPath, settings);
        if (settings.EncryptContent && settings.EncryptionKey.Length == 0)
        {
            PlayerBuildEncryptionWindow.Open(key =>
            {
                settings.EncryptionKey = key;
                ChooseOutput(settings, runAfterBuild);
            });
            return;
        }
        var buildRoot = PlayerBuildLayout.GetDefaultBuildRoot(EditorApplication.projectPath);
        EditorServiceAccess.GetRequiredService<FileUtility>().CreateDirectory(buildRoot);
        EditorFileDialog.OpenFolder(
            runAfterBuild ? "Build And Run" : $"Build Player - {settings.TargetId}",
            buildRoot,
            parent => StartBuild(settings, parent, runAfterBuild));
    }

    internal static void ChooseContentUpdateOutput(PlayerBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (IsBuilding || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return;
        PlayerBuildSettingsStore.Save(EditorApplication.projectPath, settings);
        if (settings.EncryptContent && settings.EncryptionKey.Length == 0)
        {
            PlayerBuildEncryptionWindow.Open(key =>
            {
                settings.EncryptionKey = key;
                ChooseContentUpdateOutput(settings);
            });
            return;
        }
        StartContentUpdate(settings,
            PlayerBuildLayout.GetDefaultHotResourceRoot(EditorApplication.projectPath));
    }

    internal static bool CanRunLocally(string targetId)
    {
        if (!BEngine.Platform.BuildTargetUtility.Current.TryGet(targetId, out var target)) return false;
        var platformService = EditorServiceAccess.GetRequiredService<PlatformUtility>();
        return target.Platform switch
        {
            BuildTargetPlatform.Windows => platformService.IsWindows,
            BuildTargetPlatform.Linux => platformService.IsLinux,
            BuildTargetPlatform.MacOS => platformService.IsMacOS,
            _ => false
        };
    }

    private static void StartBuild(
        PlayerBuildSettings settings,
        string parentDirectory,
        bool runAfterBuild)
    {
        if (IsBuilding) return;
        var projectPath = EditorApplication.projectPath;
        var productName = EditorServiceAccess.GetRequiredService<ProjectWorkspace>().Project.Name;
        var projectName = PlayerBuildLayout.SanitizePlayerName(productName);
        var pathService = EditorServiceAccess.GetRequiredService<PathUtility>();
        var fileSystem = EditorServiceAccess.GetRequiredService<FileUtility>();
        var output = pathService.Combine(parentDirectory, projectName);
        if (!PrepareOutput(projectPath, parentDirectory, output, productName)) return;
        var replaceExisting = fileSystem.DirectoryExists(output);
        var cancellation = new CancellationTokenSource();
        _activeBuild = cancellation;
        RaiseStateChanged();
        var progress = new Progress<PlayerBuildProgress>(value =>
            EditorApplication.QueueMainThread(() => ReportProgress(value, cancellation),
                $"Player build progress: {value.Phase}"));
        var request = new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = settings.TargetId,
            GraphicsBackends = settings.GraphicsBackends.ToArray(),
            CompileShaders = settings.CompileShaders,
            RuntimeShaderCompilation = settings.RuntimeShaderCompilation,
            HostContractPath = settings.HostContractPath,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
            WindowsGarbageCollection = settings.WindowsGarbageCollection,
            WindowsGcHeapCount = settings.WindowsGcHeapCount,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
            EncryptContent = settings.EncryptContent,
            EncryptionKey = settings.EncryptionKey,
            ReplaceExisting = replaceExisting,
            BuildVersion = settings.BuildVersion,
            HotResourceVersion = settings.HotResourceVersion,
            EnableHotUpdate = settings.EnableHotUpdate,
            ContentUpdatePolicy = settings.ContentUpdatePolicy,
            CompressAssetBundles = settings.CompressAssetBundles,
            ManagedStripping = settings.ManagedStripping,
            WritePlayerLog = settings.WritePlayerLog,
            SplashScreenEnabled = settings.SplashScreenEnabled,
            SplashImage = settings.SplashImage,
            SplashBackgroundColor = settings.SplashBackgroundColor,
            SplashMinimumDurationSeconds = settings.SplashMinimumDurationSeconds,
            CacheDirectory = settings.CacheDirectory,
            AndroidApplicationIdentifier = settings.AndroidApplicationIdentifier,
            AndroidMinimumApiLevel = settings.AndroidMinimumApiLevel,
            AndroidBuildAppBundle = settings.AndroidBuildAppBundle,
            IosBundleIdentifier = settings.IosBundleIdentifier,
            IosMinimumVersion = settings.IosMinimumVersion,
            IosCodesignKey = settings.IosCodesignKey,
            IosProvisioningProfile = settings.IosProvisioningProfile,
            HotUpdateStartupScene = settings.HotUpdateStartupScene,
            Scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings)
        };
        settings.EncryptionKey = string.Empty;
        _ = ObserveAsync(
            EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
                .BuildAsync(request, progress, cancellation.Token),
            output,
            runAfterBuild,
            cancellation, projectPath, settings);
    }

    private static void StartContentUpdate(PlayerBuildSettings settings, string output)
    {
        if (IsBuilding) return;
        try
        {
            var fileSystem = EditorServiceAccess.GetRequiredService<FileUtility>();
            if (fileSystem.FileExists(output))
                throw new IOException($"Content update output is an existing file: '{output}'.");
            if (fileSystem.DirectoryExists(output) &&
                fileSystem.EnumerateFileSystemEntries(output).Any() &&
                !EditorUtility.DisplayDialog("Build Content Update",
                    $"Publish immutable Hot Resource version '{settings.HotResourceVersion}' into:\n{output}?",
                    "Publish", "Cancel")) return;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return;
        }

        var projectPath = EditorApplication.projectPath;
        var cancellation = new CancellationTokenSource();
        _activeBuild = cancellation;
        RaiseStateChanged();
        var progress = new Progress<PlayerBuildProgress>(value =>
            EditorApplication.QueueMainThread(() => ReportProgress(value, cancellation),
                $"Content update progress: {value.Phase}"));
        var request = new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = settings.TargetId,
            GraphicsBackends = settings.GraphicsBackends.ToArray(),
            CompileShaders = settings.CompileShaders,
            RuntimeShaderCompilation = settings.RuntimeShaderCompilation,
            HostContractPath = settings.HostContractPath,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
            WindowsGarbageCollection = settings.WindowsGarbageCollection,
            WindowsGcHeapCount = settings.WindowsGcHeapCount,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
            EncryptContent = settings.EncryptContent,
            EncryptionKey = settings.EncryptionKey,
            BuildVersion = settings.BuildVersion,
            HotResourceVersion = settings.HotResourceVersion,
            EnableHotUpdate = settings.EnableHotUpdate,
            ContentUpdatePolicy = settings.ContentUpdatePolicy,
            CompressAssetBundles = settings.CompressAssetBundles,
            ManagedStripping = settings.ManagedStripping,
            WritePlayerLog = settings.WritePlayerLog,
            SplashScreenEnabled = settings.SplashScreenEnabled,
            SplashImage = settings.SplashImage,
            SplashBackgroundColor = settings.SplashBackgroundColor,
            SplashMinimumDurationSeconds = settings.SplashMinimumDurationSeconds,
            CacheDirectory = settings.CacheDirectory,
            AndroidApplicationIdentifier = settings.AndroidApplicationIdentifier,
            AndroidMinimumApiLevel = settings.AndroidMinimumApiLevel,
            AndroidBuildAppBundle = settings.AndroidBuildAppBundle,
            IosBundleIdentifier = settings.IosBundleIdentifier,
            IosMinimumVersion = settings.IosMinimumVersion,
            IosCodesignKey = settings.IosCodesignKey,
            IosProvisioningProfile = settings.IosProvisioningProfile,
            HotUpdateStartupScene = settings.HotUpdateStartupScene,
            Scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings)
        };
        settings.EncryptionKey = string.Empty;
        _ = ObserveContentAsync(
            EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
                .BuildContentUpdateAsync(request, output, progress, cancellation.Token),
            output, cancellation);
    }

    private static bool PrepareOutput(
        string projectPath,
        string parentDirectory,
        string output,
        string productName)
    {
        try
        {
            var fileSystem = EditorServiceAccess.GetRequiredService<FileUtility>();
            var pathService = EditorServiceAccess.GetRequiredService<PathUtility>();
            ValidateOutputLocation(projectPath, parentDirectory, output);
            var fullOutput = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(output));
            if (fileSystem.FileExists(fullOutput))
                throw new IOException($"The Player build output is an existing file: '{fullOutput}'.");
            if (!fileSystem.DirectoryExists(fullOutput)) return true;
            if ((fileSystem.GetAttributes(fullOutput) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A linked directory cannot be replaced by a Player build.");
            if (fileSystem.EnumerateFileSystemEntries(fullOutput).Any() &&
                !IsExistingPlayerBuild(fullOutput, productName))
                throw new InvalidOperationException(
                    $"The existing directory is not a {productName} Player build and will not be replaced: " +
                    $"'{fullOutput}'.");
            if (!EditorUtility.DisplayDialog(
                    "Build Player",
                    $"A build already exists at:\n{fullOutput}\n\nReplace it?",
                    "Replace",
                    "Cancel")) return false;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return false;
        }
    }

    internal static void ValidateOutputLocation(
        string projectPath,
        string parentDirectory,
        string output)
    {
        var pathService = EditorServiceAccess.GetRequiredService<PathUtility>();
        var fullProject = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(projectPath));
        var fullParent = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(parentDirectory));
        var fullOutput = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(output));
        if (!pathService.GetDirectoryName(fullOutput)!
                .Equals(fullParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build output escapes the selected folder: '{fullOutput}'.");
        PlayerBuildLayout.ValidateOutputLocation(fullProject, fullOutput,
            EditorServiceAccess.GetRequiredService<FileUtility>(), pathService);
    }

    internal static bool IsExistingPlayerBuild(string directory, string productName)
    {
        return IsExistingPlayerBuild(
            directory,
            productName,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());
    }

    internal static bool IsExistingPlayerBuild(
        string directory,
        string productName,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        try
        {
            var playerName = PlayerBuildLayout.SanitizePlayerName(productName);
            var dataDirectory = pathService.Combine(
                directory, $"{playerName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}");
            var runtimeRoot = fileSystem.FileExists(pathService.Combine(
                dataDirectory,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                PlayerPackagedResourceAddresses.PlayerArchiveFileName))
                ? dataDirectory
                : pathService.Combine(dataDirectory, "res");
            var manifest = fileSystem.DirectoryExists(runtimeRoot)
                ? PlayerWorkspace.OpenRuntime(runtimeRoot, fileSystem, pathService)
                    .GetRuntimeMetadata()?.PlayerBootstrap
                : null;
            if (manifest is null && fileSystem.FileExists(
                    pathService.Combine(directory, PlayerBootstrapManifest.FileName)))
                manifest = PlayerBootstrapManifestSerializer.Load(directory, fileSystem, pathService);
            if (manifest is null) return false;
            return manifest.ProductName.Equals(productName, StringComparison.Ordinal) &&
                   fileSystem.FileExists(pathService.Combine(directory, manifest.Executable)) &&
                   fileSystem.DirectoryExists(pathService.Combine(directory, manifest.DataDirectory)) &&
                   fileSystem.DirectoryExists(pathService.Combine(directory, manifest.ResourceDirectory)) &&
                   fileSystem.DirectoryExists(pathService.Combine(directory, manifest.AssemblyDirectory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or JsonException)
        {
            return false;
        }
    }

    private static void ReportProgress(
        PlayerBuildProgress progress,
        CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_activeBuild, cancellation) || cancellation.IsCancellationRequested) return;
        if (EditorUtility.DisplayCancelableProgressBar(
                "Build Player",
                progress.Message,
                Math.Clamp(progress.Progress, 0f, 1f)))
            cancellation.Cancel();
    }

    private static async Task ObserveAsync(
        Task<PlayerBuildResult> build,
        string output,
        bool runAfterBuild,
        CancellationTokenSource cancellation, string projectPath, PlayerBuildSettings settings)
    {
        PlayerBuildResult? result = null;
        Exception? failure = null;
        try { result = await build.ConfigureAwait(false); }
        catch (Exception exception) { failure = exception; }
        EditorApplication.QueueMainThread(() =>
        {
            try
            {
                EditorUtility.ClearProgressBar();
                if (failure is OperationCanceledException)
                    Debug.LogWarning("Player build was canceled.");
                else if (failure is not null)
                    Debug.LogException(failure);
                else
                {
                    Debug.Log($"Built {result!.Target.TargetId} Player at '{result.OutputDirectory}' " +
                              $"with content release '{result.ContentVersion}'.");
                    if (result.HostContractPath is { } contractPath)
                    {
                        settings.HostContractPath = contractPath;
                        PlayerBuildSettingsStore.Save(projectPath, settings);
                        Debug.Log($"Host contract for subsequent content updates: '{contractPath}'.");
                    }
                    if (runAfterBuild) RunPlayer(result);
                }
            }
            finally
            {
                if (ReferenceEquals(_activeBuild, cancellation)) _activeBuild = null;
                cancellation.Dispose();
                RaiseStateChanged();
            }
        }, $"Finish Player build: {output}");
    }

    private static async Task ObserveContentAsync(
        Task<PlayerContentUpdateResult> build,
        string output,
        CancellationTokenSource cancellation)
    {
        PlayerContentUpdateResult? result = null;
        Exception? failure = null;
        try { result = await build.ConfigureAwait(false); }
        catch (Exception exception) { failure = exception; }
        EditorApplication.QueueMainThread(() =>
        {
            try
            {
                EditorUtility.ClearProgressBar();
                if (failure is OperationCanceledException)
                    Debug.LogWarning("Content update build was canceled.");
                else if (failure is not null)
                    Debug.LogException(failure);
                else
                    Debug.Log(result!.Promoted
                        ? $"Published content release '{result.ContentVersion}' and promoted latest at " +
                          $"'{result.OutputDirectory}'."
                        : $"Published content release '{result.ContentVersion}'; latest remains " +
                          $"'{result.LatestVersion}' at '{result.OutputDirectory}'.");
            }
            finally
            {
                if (ReferenceEquals(_activeBuild, cancellation)) _activeBuild = null;
                cancellation.Dispose();
                RaiseStateChanged();
            }
        }, $"Finish content update: {output}");
    }

    private static void RunPlayer(PlayerBuildResult result)
    {
        if (!CanRunLocally(result.Target.TargetId))
        {
            Debug.LogWarning($"Build target '{result.Target.TargetId}' cannot run on this editor host.");
            return;
        }

        var platformService = EditorServiceAccess.GetRequiredService<PlatformUtility>();
        var fileSystem = EditorServiceAccess.GetRequiredService<FileUtility>();
        var pathService = EditorServiceAccess.GetRequiredService<PathUtility>();
        var executable = platformService.IsWindows
            ? fileSystem.EnumerateFiles(result.OutputDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(path => pathService.GetFileName(path)
                    .StartsWith("BEngine", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault()
            : fileSystem.EnumerateFiles(result.OutputDirectory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.IsNullOrEmpty(pathService.GetExtension(path)));
        if (executable is null)
        {
            Debug.LogError($"Build completed, but no runnable Player was found in '{result.OutputDirectory}'.");
            return;
        }
        EditorUtility.OpenWithDefaultApp(executable);
    }

    private static void RaiseStateChanged() =>
        EditorCallbackDispatcher.Invoke(stateChanged, nameof(stateChanged));

}
