using BEngine.Build;

namespace BEngine.Editor;

public sealed class WindowsBuildModule : IPlatformBuildModule
{
    public IPlayerBuildTargetProvider CreateProvider(PlatformBuildServices services) =>
        new WindowsBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths);
}

public sealed class WindowsBuildTargetProvider(
    BEngine.Platform.ProcessUtility processes,
    BEngine.Platform.ApplicationPathUtility applicationPaths,
    BEngine.Platform.FileUtility fileSystem,
    BEngine.Platform.PathUtility paths)
    : DotNetDesktopBuildTargetProvider(processes, applicationPaths, fileSystem, paths)
{
    public override string ProviderId => "bengine.windows";
    public override bool SupportsTarget(BuildTargetDescriptor target) =>
        target.Platform == BuildTargetPlatform.Windows;

    protected override void ConfigurePlatformPublish(
        BEngine.Platform.ProcessRunRequest process, PlayerBuildContext context)
    {
        if (context.Target.Platform != BuildTargetPlatform.Windows) return;
        var request = context.Request;
        PlayerGarbageCollectionBuildSettings.ValidateWindows(request.WindowsGarbageCollection, request.WindowsGcHeapCount);
        if (request.WindowsGarbageCollection == PlayerGarbageCollectionMode.RuntimeDefault) return;
        process.Arguments.Add($"-p:BEngineWindowsGarbageCollection={request.WindowsGarbageCollection}");
        if (request.WindowsGcHeapCount != 0)
            process.Arguments.Add("-p:BEngineWindowsGcHeapCount=" + request.WindowsGcHeapCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
