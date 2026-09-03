using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace BEngine.Editor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        EditorStartupReporter? startupReporter = null;
        try
        {
            EditorLogStore.Initialize();
            NativeDpiAwareness.EnablePerMonitorV2();
            if (args.Length > 0 && args[0].Equals("--convert-html", StringComparison.OrdinalIgnoreCase))
                return ConvertHtml(args);
            if (args.Length > 0 && args[0].Equals("--build-player", StringComparison.OrdinalIgnoreCase))
                return BuildPlayer(args);
            if (args.Length > 0 && args[0].Equals("--build-content-update", StringComparison.OrdinalIgnoreCase))
                return BuildContentUpdate(args);
            if (args.Length > 0 && args[0].Equals("--set-content-latest", StringComparison.OrdinalIgnoreCase))
                return SetContentLatest(args);

            if (args.Length == 0)
            {
                throw new ArgumentException("A BEngine project path is required. Start the engine with BEngine.bat.");
            }
            startupReporter = EditorStartupReporter.Create(args);
            startupReporter?.ReportStarting();
            var projectPath = Path.GetFullPath(args[0]);
            var openEditorStatus = args.Skip(1).Any(argument =>
                argument.Equals("--editor-status", StringComparison.OrdinalIgnoreCase));
            var openUiBuilder = args.Skip(1).Any(argument =>
                argument.Equals("--ui-builder", StringComparison.OrdinalIgnoreCase));
            using var instance = EditorInstanceContext.Create(projectPath);
            EditorPreferences.Initialize();
            using var loadingWindow = GpuStartupProgressWindow.Show();
            using var serviceProvider = new ServiceCollection()
                .AddBEngineEditor(new EditorLaunchOptions(projectPath, openEditorStatus, openUiBuilder))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var projectScope = serviceProvider.CreateScope();
            using var editor = projectScope.ServiceProvider.GetRequiredService<GpuEditorApplication>();
            editor.Run(() =>
            {
                loadingWindow.Complete();
                startupReporter?.TryReportReady();
            });
            return 0;
        }
        catch (Exception exception)
        {
            string? logPath = null;
            try
            {
                var candidateLogPath = EditorDataPaths.editorBootstrapLogPath;
                File.AppendAllText(candidateLogPath,
                    $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(false));
                logPath = candidateLogPath;
            }
            catch (Exception logException)
            {
                Trace.WriteLine($"BEngine Editor startup failure could not be logged: {logException}");
            }

            var commandLineBuild = args.Length > 0 &&
                                    (args[0].Equals("--build-player", StringComparison.OrdinalIgnoreCase) ||
                                     args[0].Equals("--build-content-update", StringComparison.OrdinalIgnoreCase) ||
                                     args[0].Equals("--set-content-latest", StringComparison.OrdinalIgnoreCase));
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

    private static int ConvertHtml(IReadOnlyList<string> args)
    {
        if (args.Count is < 2 or > 3)
            throw new ArgumentException(
                "Usage: BEngine.Editor.exe --convert-html <source.html> [destination.uxml]");

        var sourcePath = Path.GetFullPath(args[1]);
        var destinationPath = args.Count == 3
            ? Path.GetFullPath(args[2])
            : Path.ChangeExtension(sourcePath, ".uxml");
        var converterType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BEngine.UIElements.Editor.HtmlToUIElementsConverter"))
            .FirstOrDefault(type => type is not null) ?? throw new InvalidOperationException(
                "HTML conversion requires the optional com.bengine.ui-elements package.");
        var method = converterType.GetMethod("ConvertFile", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) ??
            throw new MissingMethodException(converterType.FullName, "ConvertFile");
        method.Invoke(null, [sourcePath, destinationPath]);
        Console.WriteLine("HTML_TO_UIELEMENTS_OK");
        Console.WriteLine($"UXML={destinationPath}");
        return 0;
    }

    private static int BuildPlayer(IReadOnlyList<string> args)
    {
        if (args.Count is < 3 or > 5)
            throw new ArgumentException(
                "Usage: BEngine.Editor --build-player <project-path> <output-directory> " +
                "[target-id] [hot-resource-version]");
        var projectPath = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(projectPath);
        var settings = PlayerBuildSettingsStore.Load(projectPath);
        var targetId = args.Count >= 4 ? args[3] : settings.TargetId;
        var hotResourceVersion = args.Count == 5
            ? PlayerBuildSettingsStore.NormalizeHotResourceVersion(args[4])
            : settings.HotResourceVersion;
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0)
            throw new InvalidDataException("BuildSettings.yaml does not contain an enabled scene.");
        var parent = Path.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Player output '{output}' has no parent directory.");
        PlayerBuildMenuCommands.ValidateOutputLocation(projectPath, parent, output);
        var replaceExisting = Directory.Exists(output);
        if (replaceExisting && Directory.EnumerateFileSystemEntries(output).Any() &&
            !PlayerBuildMenuCommands.IsExistingPlayerBuild(output, workspace.Project.Name))
            throw new InvalidOperationException(
                $"The output is not a recognized {workspace.Project.Name} Player build: '{output}'.");

        PrepareProjectForCommandLineBuild(workspace);
        var progress = new InlineProgress<PlayerBuildProgress>(value => Console.WriteLine(
            $"BENGINE_BUILD_PROGRESS|{value.Progress:P0}|{value.Phase}|{value.Message}"));
        var result = PlayerBuildPipeline.BuildAsync(new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = targetId,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
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
        var projectPath = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(projectPath);
        var settings = PlayerBuildSettingsStore.Load(projectPath);
        var targetId = args.Count >= 4 ? args[3] : settings.TargetId;
        var hotResourceVersion = args.Count == 5
            ? PlayerBuildSettingsStore.NormalizeHotResourceVersion(args[4])
            : settings.HotResourceVersion;
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0)
            throw new InvalidDataException("BuildSettings.yaml does not contain an enabled scene.");

        PrepareProjectForCommandLineBuild(workspace);
        var progress = new InlineProgress<PlayerBuildProgress>(value => Console.WriteLine(
            $"BENGINE_CONTENT_PROGRESS|{value.Progress:P0}|{value.Phase}|{value.Message}"));
        var result = PlayerContentUpdatePipeline.BuildAsync(new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = targetId,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained,
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
        var packageDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
        var version = PlayerBuildSettingsStore.NormalizeHotResourceVersion(args[2]);
        PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, version);
        Console.WriteLine(
            $"BENGINE_CONTENT_LATEST_SET_OK|package={Path.GetFileName(packageDirectory)}|" +
            $"version={version}|directory={packageDirectory}");
        return 0;
    }

    internal static void PrepareProjectForCommandLineBuild(BEngine.ProjectSystem.ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        BEngine.ProjectSystem.Editor.ProjectWorkspaceFactory.EnsureRequiredAotInvariants(workspace);
        using var packages = new BPackageManager(workspace, null, loadAssemblies: false);
        _ = ProjectScriptCompiler.CompileAndLoad(workspace);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
