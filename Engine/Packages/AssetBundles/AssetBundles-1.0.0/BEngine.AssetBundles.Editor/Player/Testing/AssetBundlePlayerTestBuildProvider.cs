using System.Runtime.CompilerServices;
using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Editor;

namespace BEngine.Editor.Build;

/// <summary>Connects AB-backed Player builds and content releases to the test runner.</summary>
public sealed class AssetBundlePlayerTestBuildProvider : IPlayerValidationBuildProvider
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register the content-backed test build workflow.")]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<IPlayerValidationBuildProvider>(new AssetBundlePlayerTestBuildProvider());

    public async Task<PlayerValidationBuild> BuildAsync(PlayerValidationBuildRequest request,
        IProgress<PlayerValidationBuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var buildRequest = CreateRequest(request);
        var bridge = progress is null ? null : new TestBuildProgress(progress);
        var build = await PlayerBuildPipeline.BuildAsync(buildRequest, bridge, cancellationToken).ConfigureAwait(false);
        await PlayerContentUpdatePipeline.BuildAsync(buildRequest, request.ContentOutputDirectory, bridge, cancellationToken)
            .ConfigureAwait(false);
        return DescribeBuiltPlayer(build, ResolveContentDirectory(request.ProjectPath, request.ContentOutputDirectory),
            buildRequest.AndroidApplicationIdentifier);
    }

    internal static string ResolveContentDirectory(string projectPath, string outputDirectory) =>
        Path.Combine(outputDirectory, PlayerContentBuildPipeline.GetProjectPackageName(projectPath));

    /// <summary>Describes an existing framework build for device/browser test execution.</summary>
    public static PlayerValidationBuild DescribeBuiltPlayer(PlayerBuildResult build, string contentDirectory,
        string androidApplicationIdentifier = "com.bengine.game")
    {
        ArgumentNullException.ThrowIfNull(build);
        return new PlayerValidationBuild(build.Target, build.OutputDirectory, contentDirectory)
        {
            ExecutablePath = FindExecutable(build.OutputDirectory, build.Target.Platform),
            AndroidApplicationIdentifier = androidApplicationIdentifier,
            LaunchOptions = new PlayerValidationLaunchOptions
            {
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["BENGINE_PLAYER_AUTO_ADVANCE_STARTUP"] = "1",
                    ["BENGINE_ASSET_BUNDLE_ALLOW_INSECURE_LOOPBACK"] = "1"
                },
                ContentUrlEnvironmentVariable = "BENGINE_ASSET_BUNDLE_REMOTE_URL",
                BrowserQueryParameters = new Dictionary<string, string> { ["bengineAutoStart"] = "1" },
                AndroidBooleanExtras = new Dictionary<string, bool> { ["bengineAutoStart"] = true }
            }
        };
    }

    internal static PlayerBuildRequest CreateRequest(PlayerValidationBuildRequest request)
    {
        var settings = PlayerBuildSettingsStore.Load(request.ProjectPath);
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0) throw new InvalidDataException("Build Settings contains no enabled scene.");
        return new PlayerBuildRequest
        {
            ProjectPath = request.ProjectPath,
            OutputDirectory = request.OutputDirectory,
            TargetId = request.TargetId ?? settings.TargetId,
            GraphicsBackends = request.TargetId is null ? settings.GraphicsBackends.ToArray() : [],
            CompileShaders = settings.CompileShaders,
            RuntimeShaderCompilation = settings.RuntimeShaderCompilation,
            HostContractPath = settings.HostContractPath,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained || (request.TargetId ?? settings.TargetId).StartsWith("android", StringComparison.OrdinalIgnoreCase),
            WindowsGarbageCollection = settings.WindowsGarbageCollection,
            WindowsGcHeapCount = settings.WindowsGcHeapCount,
            WebScriptCompilation = settings.WebScriptCompilation,
            WebFontBackend = settings.WebFontBackend,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
            ReplaceExisting = false,
            BuildVersion = settings.BuildVersion,
            HotResourceVersion = settings.HotResourceVersion,
            EnableHotUpdate = settings.EnableHotUpdate,
            ContentUpdatePolicy = settings.ContentUpdatePolicy,
            ContentOptions = new Dictionary<string, string>(settings.ContentOptions, StringComparer.Ordinal),
            ManagedStripping = settings.ManagedStripping,
            WritePlayerLog = true,
            SplashScreenEnabled = false,
            SplashImage = settings.SplashImage,
            SplashBackgroundColor = settings.SplashBackgroundColor,
            SplashMinimumDurationSeconds = 0,
            CacheDirectory = settings.CacheDirectory,
            AndroidApplicationIdentifier = settings.AndroidApplicationIdentifier,
            AndroidMinimumApiLevel = settings.AndroidMinimumApiLevel,
            AndroidBuildAppBundle = false,
            AdditionalMsBuildProperties = new Dictionary<string, string> { ["BEngineDevelopmentHost"] = "true" },
            IosBundleIdentifier = settings.IosBundleIdentifier,
            IosMinimumVersion = settings.IosMinimumVersion,
            Scenes = scenes
        };
    }

    private static string FindExecutable(string output, BuildTargetPlatform platform)
    {
        if (platform is BuildTargetPlatform.Android or BuildTargetPlatform.IOS or BuildTargetPlatform.Web) return string.Empty;
        var files = Directory.GetFiles(output, platform == BuildTargetPlatform.Windows ? "*.exe" : "BEngine.Application",
            platform == BuildTargetPlatform.Windows ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories);
        return files.Length == 1 ? files[0] : throw new FileNotFoundException(
            $"Expected one Player executable in '{output}', found {files.Length}.");
    }

    private sealed class TestBuildProgress(IProgress<PlayerValidationBuildProgress> target) : IProgress<PlayerBuildProgress>
    {
        public void Report(PlayerBuildProgress value) => target.Report(new(value.Phase.ToString(), value.Message, value.Progress));
    }
}
