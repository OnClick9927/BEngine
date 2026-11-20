using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;
namespace BEngine.Editor.Build;
internal static class PlayerCommandLine
{
    internal static bool TryRun(IReadOnlyList<string> args, out int result)
    {
        result = 0;
        if(args.Count == 0) return false;
        switch(args[0].ToLowerInvariant())
        {
            case "--build-player": result = BuildPlayer(args); return true;
            case "--build-content-update": result = BuildContentUpdate(args); return true;
            case "--set-content-latest": result = SetContentLatest(args); return true;
            default: return false;
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
            ? BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(args[4])
            : settings.HotResourceVersion;
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0)
            throw new InvalidDataException("BuildSettings.yaml does not contain an enabled scene.");
        var parent = pathService.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Player output '{output}' has no parent directory.");
        PlayerBuildMenuCommands.ValidateOutputLocation(projectPath, parent, output);
        using var projectScope = PrepareProjectForCommandLineBuild(workspace);
        var replaceExisting = fileSystem.DirectoryExists(output);
        if (replaceExisting && fileSystem.EnumerateFileSystemEntries(output).Any() &&
            !PlayerBuildMenuCommands.IsExistingPlayerBuild(
                output, workspace.Project.Name, fileSystem, pathService))
            throw new InvalidOperationException(
                $"The output is not a recognized {workspace.Project.Name} Player build: '{output}'.");

        var progress = new InlineProgress<PlayerBuildProgress>(value => Console.WriteLine(
            $"BENGINE_BUILD_PROGRESS|{value.Progress:P0}|{value.Phase}|{value.Message}"));
        var result = EditorCapabilities.GetRequiredService<IPlayerBuildPipeline>().BuildAsync(
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
            ContentOptions = new Dictionary<string, string>(settings.ContentOptions, StringComparer.Ordinal),
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
            ? BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(args[4])
            : settings.HotResourceVersion;
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0)
            throw new InvalidDataException("BuildSettings.yaml does not contain an enabled scene.");

        using var projectScope = PrepareProjectForCommandLineBuild(workspace);
        var progress = new InlineProgress<PlayerBuildProgress>(value => Console.WriteLine(
            $"BENGINE_CONTENT_PROGRESS|{value.Progress:P0}|{value.Phase}|{value.Message}"));
        var result = EditorCapabilities.GetRequiredService<IPlayerBuildPipeline>().BuildContentUpdateAsync(
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
            ContentOptions = new Dictionary<string, string>(settings.ContentOptions, StringComparer.Ordinal),
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
        var version = BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(args[2]);
        EditorCapabilities.GetRequiredService<IPlayerBuildPipeline>()
            .SetLatestContentVersion(packageDirectory, version);
        Console.WriteLine(
            $"BENGINE_CONTENT_LATEST_SET_OK|package={pathService.GetFileName(packageDirectory)}|" +
            $"version={version}|directory={packageDirectory}");
        return 0;
    }

    internal static IDisposable PrepareProjectForCommandLineBuild(BEngine.ProjectSystem.ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var session = ProjectToolSession.Open(workspace);
        try
        {
            PlayerProjectPolicies.Ensure(workspace);
            session.InitializeScripts();
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility PathService => EditorCapabilities.GetRequiredService<PathUtility>();
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
