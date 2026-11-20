using BEngine.Build;

namespace BEngine.Editor;

public sealed class macOSBuildModule : IPlatformBuildModule
{
    public IPlatformBuildTargetProvider CreateProvider(PlatformBuildServices services) =>
        new macOSBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths, services.Capabilities);
}

public sealed class macOSBuildTargetProvider(
    BEngine.Platform.ProcessUtility processes,
    BEngine.Platform.ApplicationPathUtility applicationPaths,
    BEngine.Platform.FileUtility fileSystem,
    BEngine.Platform.PathUtility paths,
    PlatformPlayerBuildCapabilities capabilities)
    : DotNetDesktopBuildTargetProvider(processes, applicationPaths, fileSystem, paths, capabilities)
{
    public override string ProviderId => "bengine.macos";
    public override bool SupportsTarget(BuildTargetDescriptor target) =>
        target.Platform == BuildTargetPlatform.MacOS;
}
