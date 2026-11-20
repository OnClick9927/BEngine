using BEngine.Build;

namespace BEngine.Editor;

public sealed class iOSBuildModule : IPlatformBuildModule
{
    public IPlatformBuildTargetProvider CreateProvider(PlatformBuildServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new DotNetIosBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths, services.Platform, services.Capabilities);
    }
}
