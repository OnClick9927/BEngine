using BEngine.Build;

namespace BEngine.Editor;

public sealed class WebBuildModule : IPlatformBuildModule
{
    public IPlatformBuildTargetProvider CreateProvider(PlatformBuildServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new DotNetWebAssemblyBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths, services.Platform, services.Capabilities);
    }
}
