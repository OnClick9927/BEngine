using BEngine.Build;

namespace BEngine.Editor;

public sealed class LinuxBuildModule : IPlatformBuildModule
{
    public IPlayerBuildTargetProvider CreateProvider(PlatformBuildServices services) =>
        new LinuxBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths);
}

public sealed class LinuxBuildTargetProvider(
    BEngine.Platform.ProcessUtility processes,
    BEngine.Platform.ApplicationPathUtility applicationPaths,
    BEngine.Platform.FileUtility fileSystem,
    BEngine.Platform.PathUtility paths)
    : DotNetDesktopBuildTargetProvider(processes, applicationPaths, fileSystem, paths)
{
    public override string ProviderId => "bengine.linux";
    public override bool SupportsTarget(BuildTargetDescriptor target) =>
        target.Platform == BuildTargetPlatform.Linux;
}
