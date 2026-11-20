using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Adapts the engine build request to a platform without assuming a content format.</summary>
public class PlayerPlatformBuildTargetAdapter(
    IPlatformBuildTargetProvider provider, FileUtility fileSystem, PathUtility paths)
    : IPlayerBuildTargetProvider, IPlayerBuildPrerequisiteProvider
{
    protected FileUtility FileSystem { get; } = fileSystem;
    protected PathUtility Paths { get; } = paths;

    public string ProviderId => provider.ProviderId;
    public bool CanBuild(BuildTargetDescriptor target, out string reason) => provider.CanBuild(target, out reason);
    public bool SupportsTarget(BuildTargetDescriptor target) =>
        provider is IPlayerBuildPrerequisiteProvider prerequisites
            ? prerequisites.SupportsTarget(target) : provider.CanBuild(target, out _);
    public IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(BuildTargetDescriptor target) =>
        provider is IPlayerBuildPrerequisiteProvider prerequisites ? prerequisites.GetPrerequisites(target) : [];
    public async Task BuildAsync(PlayerBuildContext context, CancellationToken cancellationToken = default)
    {
        var workspace = ProjectWorkspace.Open(context.Request.ProjectPath, FileSystem, Paths);
        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory, FileSystem, Paths);
        var request = new PlatformBuildRequest
        {
            Options = new PlatformBuildOptions()
                .Set(new WebBuildOptions
                {
                    WebFontBackend = context.Request.WebFontBackend,
                    WebScriptCompilation = context.Request.WebScriptCompilation,
                })
                .Set(new WindowsBuildOptions
                {
                    WindowsGarbageCollection = context.Request.WindowsGarbageCollection,
                    WindowsGcHeapCount = context.Request.WindowsGcHeapCount,
                })
                .Set(new AndroidBuildOptions
                {
                    AndroidApplicationIdentifier = context.Request.AndroidApplicationIdentifier,
                    AndroidMinimumApiLevel = context.Request.AndroidMinimumApiLevel,
                    AndroidBuildAppBundle = context.Request.AndroidBuildAppBundle,
                })
                .Set(new IosBuildOptions
                {
                    IosBundleIdentifier = context.Request.IosBundleIdentifier,
                    IosMinimumVersion = context.Request.IosMinimumVersion,
                    IosCodesignKey = context.Request.IosCodesignKey,
                    IosProvisioningProfile = context.Request.IosProvisioningProfile,
                }),
            ProjectPath = workspace.RootPath,
            TargetId = context.Request.TargetId,
            Configuration = context.Request.Configuration,
            SelfContained = context.Request.SelfContained,
            IncludeDebugSymbols = context.Request.IncludeDebugSymbols,
            PackagingRequirements = context.PackagingRequirements,
            PlayerHostProjectPath = context.Request.PlayerHostProjectPath,
            AdditionalMsBuildProperties = context.Request.AdditionalMsBuildProperties,
            RuntimeShaderCompilation = context.Request.RuntimeShaderCompilation,
            ManagedStripping = context.Request.ManagedStripping,
            SplashBackgroundColor = context.Request.SplashBackgroundColor,
            HttpStreamingAssets = UsesHttpStreamingAssets(context),
            EngineRuntimeRoot = PlayerWorkflowHostLocator.ResolveExportedEngineRoot(
                PlayerWorkflowHostLocator.Resolve(context.Target, context.Request.PlayerHostProjectPath))
        };
        var mobileNativeImages = context.Target.Platform is BuildTargetPlatform.Android or BuildTargetPlatform.IOS ||
            context.Target.Platform == BuildTargetPlatform.Web && context.Request.WebScriptCompilation == WebScriptCompilationMode.Interpreter
            ? NativeRuntimeImages.SelectClosure(context.RuntimeManagedCodeAssemblyPaths) : [];
        var platformContext = new PlatformBuildContext(request, context.Target, context.StagingDirectory,
            new PlatformBuildLayout(layout.DataDirectoryName, layout.DataDirectory, layout.AssemblyDirectory,
                layout.ResourcesDirectory, layout.StreamingAssetsDirectory, layout.GetExecutablePath(context.Target.Platform)),
            context.Progress)
        {
            StartupLogoPng = context.StartupLogoPng,
            ApplicationIconPng = context.ApplicationIconPng,
            GraphicsBackends = context.GraphicsBackends,
            RuntimeManagedCodeAssemblyPaths = context.RuntimeManagedCodeAssemblyPaths,
            PrecompiledManagedCodeAssemblyPaths = mobileNativeImages.Count > 0
                ? mobileNativeImages : context.WebPrecompiledManagedCodeAssemblyPaths,
            PrecompiledManagedCodeReferencePaths = mobileNativeImages.Count > 0
                ? mobileNativeImages : context.WebPrecompiledManagedCodeReferencePaths,
            PrecompiledSharedManagedCodeAssemblyPaths = mobileNativeImages.Count > 0
                ? mobileNativeImages : context.WebPrecompiledSharedManagedCodeAssemblyPaths,
            IncludeTextBindings = context.IncludeTextBindings,
            GeneratedCodeNamespace = "BEngine.Player.Hosts.Generated",
            Hooks = CreateBuildHooks(context)
        };
        await provider.BuildAsync(platformContext, cancellationToken).ConfigureAwait(false);
        context.IncludeTextBindings = platformContext.IncludeTextBindings;
        ValidateOutput(context);
    }

    /// <summary>Optional managed images supplied by this content format. Host publication remains engine-owned.</summary>
    protected virtual Func<string, IReadOnlyCollection<string>,
        IReadOnlyDictionary<string, PlatformPackagedAssemblySource>>? PackagedAssemblySources => null;

    protected virtual PlatformBuildHooks CreateBuildHooks(PlayerBuildContext context) =>
        new PlayerPlatformBuildHooks(context, FileSystem, Paths).Create(PackagedAssemblySources);
    protected virtual bool UsesHttpStreamingAssets(PlayerBuildContext context) => false;
    protected virtual void ValidateOutput(PlayerBuildContext context) { }
}
