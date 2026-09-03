using BEngine.Build;
using BEngine.Content;
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
        var buildRoot = PlayerBuildLayout.GetDefaultBuildRoot(EditorApplication.projectPath);
        Directory.CreateDirectory(buildRoot);
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
        StartContentUpdate(settings,
            PlayerBuildLayout.GetDefaultHotResourceRoot(EditorApplication.projectPath));
    }

    internal static bool CanRunLocally(string targetId)
    {
        if (!BuildTargetCatalog.TryGet(targetId, out var target)) return false;
        return target.Platform switch
        {
            BuildTargetPlatform.Windows => OperatingSystem.IsWindows(),
            BuildTargetPlatform.Linux => OperatingSystem.IsLinux(),
            BuildTargetPlatform.MacOS => OperatingSystem.IsMacOS(),
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
        var productName = ProjectWorkspace.Open(projectPath).Project.Name;
        var projectName = PlayerBuildLayout.SanitizePlayerName(productName);
        var output = Path.Combine(parentDirectory, projectName);
        if (!PrepareOutput(projectPath, parentDirectory, output, productName)) return;
        var replaceExisting = Directory.Exists(output);
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
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
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
            Scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings)
        };
        _ = ObserveAsync(
            PlayerBuildPipeline.BuildAsync(request, progress, cancellation.Token),
            output,
            runAfterBuild,
            cancellation);
    }

    private static void StartContentUpdate(PlayerBuildSettings settings, string output)
    {
        if (IsBuilding) return;
        try
        {
            if (File.Exists(output))
                throw new IOException($"Content update output is an existing file: '{output}'.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any() &&
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
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
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
            Scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings)
        };
        _ = ObserveContentAsync(
            PlayerContentUpdatePipeline.BuildAsync(request, output, progress, cancellation.Token),
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
            ValidateOutputLocation(projectPath, parentDirectory, output);
            var fullOutput = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
            if (File.Exists(fullOutput))
                throw new IOException($"The Player build output is an existing file: '{fullOutput}'.");
            if (!Directory.Exists(fullOutput)) return true;
            if ((File.GetAttributes(fullOutput) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A linked directory cannot be replaced by a Player build.");
            if (Directory.EnumerateFileSystemEntries(fullOutput).Any() &&
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
        var fullProject = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));
        var fullOutput = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (!Path.GetDirectoryName(fullOutput)!.Equals(fullParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build output escapes the selected folder: '{fullOutput}'.");
        var projectBuildRoot = Path.Combine(fullProject, PlayerBuildLayout.BuildDirectoryName);
        var isDedicatedProjectBuild = IsSameOrChild(fullOutput, projectBuildRoot);
        if (IsSameOrChild(fullProject, fullOutput) ||
            IsSameOrChild(fullOutput, fullProject) && !isDedicatedProjectBuild)
            throw new InvalidOperationException(
                "Player builds inside the project are only allowed under its dedicated Build directory.");
    }

    internal static bool IsExistingPlayerBuild(string directory, string productName)
    {
        try
        {
            var playerName = PlayerBuildLayout.SanitizePlayerName(productName);
            var dataDirectory = Path.Combine(
                directory, $"{playerName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}");
            var runtimeRoot = File.Exists(Path.Combine(
                dataDirectory,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                PlayerPackagedResourceAddresses.PlayerArchiveFileName))
                ? dataDirectory
                : Path.Combine(dataDirectory, "res");
            var manifest = Directory.Exists(runtimeRoot)
                ? ProjectWorkspace.OpenRuntime(runtimeRoot).RuntimeMetadata?.PlayerBootstrap
                : null;
            if (manifest is null && File.Exists(Path.Combine(directory, PlayerBootstrapManifest.FileName)))
                manifest = PlayerBootstrapManifestSerializer.Load(directory);
            if (manifest is null) return false;
            return manifest.ProductName.Equals(productName, StringComparison.Ordinal) &&
                   File.Exists(Path.Combine(directory, manifest.Executable)) &&
                   Directory.Exists(Path.Combine(directory, manifest.DataDirectory)) &&
                   Directory.Exists(Path.Combine(directory, manifest.ResourceDirectory)) &&
                   Directory.Exists(Path.Combine(directory, manifest.AssemblyDirectory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or JsonException)
        {
            return false;
        }
    }

    private static bool IsSameOrChild(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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
        CancellationTokenSource cancellation)
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

        var executable = OperatingSystem.IsWindows()
            ? Directory.EnumerateFiles(result.OutputDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path).StartsWith("BEngine", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault()
            : Directory.EnumerateFiles(result.OutputDirectory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.IsNullOrEmpty(Path.GetExtension(path)));
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
