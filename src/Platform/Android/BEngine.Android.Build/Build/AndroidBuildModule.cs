using BEngine.Build;

namespace BEngine.Editor;

public sealed class AndroidBuildModule : IPlatformBuildModule
{
    public IPlatformBuildTargetProvider CreateProvider(PlatformBuildServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new DotNetAndroidBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths, services.Platform, services.Capabilities);
    }
}
