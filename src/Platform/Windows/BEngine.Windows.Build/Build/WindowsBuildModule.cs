using BEngine.Build;

namespace BEngine.Editor;

public sealed class WindowsBuildModule : IPlatformBuildModule
{
    public IPlatformBuildTargetProvider CreateProvider(PlatformBuildServices services) =>
        new WindowsBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths, services.Capabilities);
}

public sealed class WindowsBuildTargetProvider(
    BEngine.Platform.ProcessUtility processes,
    BEngine.Platform.ApplicationPathUtility applicationPaths,
    BEngine.Platform.FileUtility fileSystem,
    BEngine.Platform.PathUtility paths,
    PlatformPlayerBuildCapabilities capabilities)
    : DotNetDesktopBuildTargetProvider(processes, applicationPaths, fileSystem, paths, capabilities)
{
    public override string ProviderId => "bengine.windows";
    public override bool SupportsTarget(BuildTargetDescriptor target) =>
        target.Platform == BuildTargetPlatform.Windows;

    protected override void ConfigurePlatformPublish(
        BEngine.Platform.ProcessRunRequest process, PlatformBuildContext context)
    {
        if (context.Target.Platform != BuildTargetPlatform.Windows) return;
        var request = context.Request;
        PlayerGarbageCollectionBuildSettings.ValidateWindows(request.Options.Get<WindowsBuildOptions>().WindowsGarbageCollection, request.Options.Get<WindowsBuildOptions>().WindowsGcHeapCount);
        if (request.Options.Get<WindowsBuildOptions>().WindowsGarbageCollection == PlayerGarbageCollectionMode.RuntimeDefault) return;
        process.Arguments.Add($"-p:BEngineWindowsGarbageCollection={request.Options.Get<WindowsBuildOptions>().WindowsGarbageCollection}");
        if (request.Options.Get<WindowsBuildOptions>().WindowsGcHeapCount != 0)
            process.Arguments.Add("-p:BEngineWindowsGcHeapCount=" + request.Options.Get<WindowsBuildOptions>().WindowsGcHeapCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
