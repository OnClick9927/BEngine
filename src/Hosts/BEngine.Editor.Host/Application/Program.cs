using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        BEngine.Tooling.ToolEnvironment.ConfigureEditor();
        if (EditorCommandLineHelp.TryHandle(args)) return 0;
        // Set the shell identity before creating any project-loading windows.
        WindowsEditorTaskbar.InitializeProcess();
        WindowsPlatform.Initialize();
        WindowsEditorPlatform.Initialize();
        RegisterPublishedResourceRoot();
        EditorHostPackages.Initialize();
        using var startupPlatformServices = new ServiceCollection()
            .AddBEngineBuildPipelines()
            .AddBEngineEditorCompilation()
            .AddBEngineEditorPlatformServices()
            .AddBEngineEditorPlayServices()
            .BuildServiceProvider();
        using var startupServiceAttachment = EditorServiceAccess.Attach(startupPlatformServices);
        EditorStartupReporter? startupReporter = null;
        try
        {
            EditorLogStore.Initialize();
            NativeDpiAwareness.EnablePerMonitorV2();
            if (args.Length > 0 && args[0].Equals("--build-player", StringComparison.OrdinalIgnoreCase))
                return BuildPlayer(args);
            if (args.Length > 0 && args[0].Equals("--build-content-update", StringComparison.OrdinalIgnoreCase))
                return BuildContentUpdate(args);
            if (args.Length > 0 && args[0].Equals("--set-content-latest", StringComparison.OrdinalIgnoreCase))
                return SetContentLatest(args);
            if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
                return EditorPackageCommandLine.Run(args);

            if (args.Length == 0)
            {
                throw new ArgumentException("A BEngine project path is required. Start the engine with Engine/Engine/BEngine.Hub.exe.");
            }
            startupReporter = EditorStartupReporter.Create(args);
            startupReporter?.ReportStarting();
            var projectPath = PathService.GetFullPath(args[0]);
            var openEditorStatus = args.Skip(1).Any(argument =>
                argument.Equals("--editor-status", StringComparison.OrdinalIgnoreCase));
            var openUiBuilder = args.Skip(1).Any(argument =>
                argument.Equals("--ui-builder", StringComparison.OrdinalIgnoreCase));
            using var instance = EditorInstanceContext.Create(projectPath);
            EditorPreferences.Initialize();
            using var loadingWindow = GpuStartupProgressWindow.Show(
                startupPlatformServices.GetRequiredService<NativeWindowUtility>(),
                startupPlatformServices.GetRequiredService<ExecutionUtility>());
            using var serviceProvider = new ServiceCollection()
                .AddBEngineEditor(new EditorLaunchOptions(projectPath, openEditorStatus, openUiBuilder))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var projectScope = serviceProvider.CreateScope();
            GpuEditorApplication editor;
            using (EditorFeatureGuard.BeginLoopScope("BEngine.Editor startup"))
                editor = projectScope.ServiceProvider.GetRequiredService<GpuEditorApplication>();
            try
            {
                editor.Run(() =>
                {
                    loadingWindow.Complete();
                    startupReporter?.TryReportReady();
                });
            }
            finally
            {
                try { editor.Dispose(); }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"BENGINE_EDITOR_SHUTDOWN_FAILED|{exception}");
                    throw;
                }
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BENGINE_EDITOR_FAILED|{exception}");
            string? logPath = null;
            try
            {
                // Project construction or disposal may have failed. Diagnostics must use
                // the bootstrap services, whose lifetime encloses this catch block.
                using var diagnosticsServices = EditorServiceAccess.Attach(startupPlatformServices);
                var fileSystem = FileSystem;
                var platformService = PlatformService;
                var projectArgument = args.Length > 0 &&
                                      (args[0].Equals("--build-player", StringComparison.OrdinalIgnoreCase) ||
                                       args[0].Equals("--build-content-update", StringComparison.OrdinalIgnoreCase))
                    ? args.ElementAtOrDefault(1)
                    : args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
                        ? args[0]
                        : null;
                var candidateLogPath = projectArgument is null
                    ? EditorDataPaths.editorBootstrapLogPath
                    : EditorDataPaths.GetProjectLogPath(projectArgument, "EditorBootstrap.log");
                using var stream = fileSystem.OpenFile(
                    candidateLogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    4096,
                    FileOptions.SequentialScan);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false),
                    bufferSize: 4096, leaveOpen: true);
                writer.Write($"[{platformService.UtcNow.ToLocalTime():O}] {exception}" +
                             platformService.NewLine);
                writer.Flush();
                fileSystem.Flush(stream);
                logPath = candidateLogPath;
            }
            catch (Exception logException)
            {
                Trace.WriteLine($"BEngine Editor startup failure could not be logged: {logException}");
            }

            var commandLineBuild = args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal);
            var reportedToLauncher = startupReporter?.TryReportFailure(exception, logPath) == true;
            if (commandLineBuild)
                Console.Error.WriteLine($"BENGINE_PLAYER_BUILD_FAILED|{exception}");
            else if (!reportedToLauncher)
            {
                var logInformation = logPath is null ? string.Empty : $"\n\nLog: {logPath}";
                try
                {
                    NativeStartupDialog.ShowError("BEngine",
                        $"BEngine Editor failed to start.\n\n{exception.GetBaseException().Message}" +
                        logInformation);
                }
                catch (Exception dialogException)
                {
                    Trace.WriteLine($"BEngine Editor startup dialog failed: {dialogException}");
                }
            }
            return 1;
        }
        finally
        {
            startupReporter?.Dispose();
            try
            {
                EditorUtility.ClearProgressBar();
            }
            catch (Exception progressException)
            {
                Trace.WriteLine($"BEngine Editor progress cleanup failed: {progressException}");
            }
        }
    }

    private static void RegisterPublishedResourceRoot()
    {
        foreach (var directory in new[] { Path.GetDirectoryName(Environment.ProcessPath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            if (!File.Exists(Path.Combine(directory!, "Resources", "Shaders", "Startup",
                    "TextureProbe.vulkan.vert.glsl"))) continue;
            EditorResource.RegisterResourceRoot(directory!);
            return;
        }
    }

    private static int BuildPlayer(IReadOnlyList<string> args)
    {
        if (args.Count is < 3 or > 5)
            throw new ArgumentException(
                "Usage: BEngine.Editor --build-player <project-path> <output-directory> " +
                "[target-id] [hot-resource-version]");
        var fileSystem = FileSystem;
        var pathService = PathService;
        var projectPath = pathService.GetFullPath(args[1]);
        var output = pathService.GetFullPath(args[2]);
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(
            projectPath, fileSystem, pathService);
        var settings = PlayerBuildSettingsStore.Load(projectPath);
        var targetId = args.Count >= 4 ? args[3] : settings.TargetId;
        var hotResourceVersion = args.Count == 5
            ? PlayerBuildSettingsStore.NormalizeHotResourceVersion(args[4])
            : settings.HotResourceVersion;
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0)
            throw new InvalidDataException("BuildSettings.yaml does not contain an enabled scene.");
        var parent = pathService.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Player output '{output}' has no parent directory.");
        PlayerBuildMenuCommands.ValidateOutputLocation(projectPath, parent, output);
        var replaceExisting = fileSystem.DirectoryExists(output);
        if (replaceExisting && fileSystem.EnumerateFileSystemEntries(output).Any() &&
            !PlayerBuildMenuCommands.IsExistingPlayerBuild(
                output, workspace.Project.Name, fileSystem, pathService))
            throw new InvalidOperationException(
                $"The output is not a recognized {workspace.Project.Name} Player build: '{output}'.");

        using var projectScope = PrepareProjectForCommandLineBuild(workspace);
        var progress = new InlineProgress<PlayerBuildProgress>(value => Console.WriteLine(
            $"BENGINE_BUILD_PROGRESS|{value.Progress:P0}|{value.Phase}|{value.Message}"));
        var result = EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>().BuildAsync(
            new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = targetId,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
            WindowsGarbageCollection = settings.WindowsGarbageCollection,
            WindowsGcHeapCount = settings.WindowsGcHeapCount,
            WebScriptCompilation = settings.WebScriptCompilation,
            WebFontBackend = settings.WebFontBackend,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
            ReplaceExisting = replaceExisting,
            BuildVersion = settings.BuildVersion,
            HotResourceVersion = hotResourceVersion,
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
            Scenes = scenes
        }, progress).ConfigureAwait(false).GetAwaiter().GetResult();
        Console.WriteLine(
            $"BENGINE_PLAYER_BUILD_OK|{result.Target.TargetId}|{result.ContentVersion}|{result.OutputDirectory}|" +
            $"hot-resource-version={hotResourceVersion}");
        return 0;
    }

    private static int BuildContentUpdate(IReadOnlyList<string> args)
    {
        if (args.Count is < 3 or > 5)
            throw new ArgumentException(
                "Usage: BEngine.Editor --build-content-update <project-path> <output-directory> " +
                "[target-id] [hot-resource-version]");
        var fileSystem = FileSystem;
        var pathService = PathService;
        var projectPath = pathService.GetFullPath(args[1]);
        var output = pathService.GetFullPath(args[2]);
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(
            projectPath, fileSystem, pathService);
        var settings = PlayerBuildSettingsStore.Load(projectPath);
        var targetId = args.Count >= 4 ? args[3] : settings.TargetId;
        var hotResourceVersion = args.Count == 5
            ? PlayerBuildSettingsStore.NormalizeHotResourceVersion(args[4])
            : settings.HotResourceVersion;
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0)
            throw new InvalidDataException("BuildSettings.yaml does not contain an enabled scene.");

        using var projectScope = PrepareProjectForCommandLineBuild(workspace);
        var progress = new InlineProgress<PlayerBuildProgress>(value => Console.WriteLine(
            $"BENGINE_CONTENT_PROGRESS|{value.Progress:P0}|{value.Phase}|{value.Message}"));
        var result = EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>().BuildContentUpdateAsync(
            new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = targetId,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
            WindowsGarbageCollection = settings.WindowsGarbageCollection,
            WindowsGcHeapCount = settings.WindowsGcHeapCount,
            WebScriptCompilation = settings.WebScriptCompilation,
            WebFontBackend = settings.WebFontBackend,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
            BuildVersion = settings.BuildVersion,
            HotResourceVersion = hotResourceVersion,
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
            Scenes = scenes
        }, output, progress).ConfigureAwait(false).GetAwaiter().GetResult();
        Console.WriteLine(
            $"BENGINE_CONTENT_UPDATE_OK|{result.ContentVersion}|{result.OutputDirectory}|" +
            $"hot-resource-version={hotResourceVersion}|latest={result.LatestVersion}|" +
            $"promoted={result.Promoted.ToString().ToLowerInvariant()}");
        return 0;
    }

    private static int SetContentLatest(IReadOnlyList<string> args)
    {
        if (args.Count != 3)
            throw new ArgumentException(
                "Usage: BEngine.Editor --set-content-latest <package-directory> <version>");
        var pathService = PathService;
        var packageDirectory = pathService.TrimEndingDirectorySeparator(
            pathService.GetFullPath(args[1]));
        var version = PlayerBuildSettingsStore.NormalizeHotResourceVersion(args[2]);
        EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
            .SetLatestContentVersion(packageDirectory, version);
        Console.WriteLine(
            $"BENGINE_CONTENT_LATEST_SET_OK|package={pathService.GetFileName(packageDirectory)}|" +
            $"version={version}|directory={packageDirectory}");
        return 0;
    }

    internal static IDisposable PrepareProjectForCommandLineBuild(BEngine.ProjectSystem.ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        BEngine.Application.dataPath = workspace.AssetsPath;
        BEngine.BAsset.ClearLoadedAssets();
        // Importers are editor extensions. Keep their assemblies and registrations alive
        // throughout both Player and content builds, just as in an open editor project.
        var packages = new BEngine.ProjectSystem.BPackageManager(workspace);
        try
        {
            BEngine.ProjectSystem.Editor.PlayerProjectTemplate.EnsureRequiredAotInvariants(workspace);
            var compiler = EditorServiceAccess.GetRequiredService<IProjectScriptCompiler>();
            var runtime = compiler.CompileRuntimeAndLoad(workspace);
            _ = compiler.CompileEditorAndLoad(workspace, runtime, typeof(EditorWindow).Assembly.Location);
            EditorInitialization.Run(scriptsReloaded: false);
            return packages;
        }
        catch { packages.Dispose(); throw; }
    }

    private static FileUtility FileSystem =>
        EditorServiceAccess.GetRequiredService<FileUtility>();

    private static PathUtility PathService =>
        EditorServiceAccess.GetRequiredService<PathUtility>();

    private static PlatformUtility PlatformService =>
        EditorServiceAccess.GetRequiredService<PlatformUtility>();

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
