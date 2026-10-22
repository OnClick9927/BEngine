using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed class iOSBuildModule : IPlatformBuildModule
{
    public IPlayerBuildTargetProvider CreateProvider(PlatformBuildServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        PlatformAssetImportDefaults.RegisterMobile(BuildTargetPlatform.IOS);
        const string targetId = "ios-arm64";
        if (!services.Capabilities.TryGetRenderer(targetId, out _))
        {
            var hostProject = FindHostProject(services);
            if (hostProject is not null)
                services.Capabilities.RegisterRenderer(new PlatformPlayerRendererCapability(
                    "bengine.ios.metal", targetId, [GraphicsBackend.Metal], hostProject,
                    SupportsHybridAotInterpreter: true,
                    InterpreterBridgeAssemblyName: "BEngine.Player.iOSHost"));
        }
        return new DotNetIosBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
            services.FileSystem, services.Paths, services.Platform, services.Capabilities);
    }

    private static string? FindHostProject(PlatformBuildServices services)
    {
        foreach (var start in new[] { services.Platform.BaseDirectory, services.Platform.CurrentDirectory })
        for (var directory = services.Paths.GetFullPath(start); directory is not null;
             directory = services.Paths.GetDirectoryName(directory))
        {
            foreach (var candidate in new[]
                     {
                         services.Paths.Combine(directory, "src", "Platform", "Hosts", "iOS",
                             "BEngine.Player.iOSHost.csproj"),
                         services.Paths.Combine(directory, "Engine", "src", "Platform", "Hosts", "iOS",
                             "BEngine.Player.iOSHost.csproj"),
                         services.Paths.Combine(directory, "BuildHosts", "iOS",
                             "BEngine.Player.iOSHost.csproj")
                     })
                if (services.FileSystem.FileExists(candidate)) return candidate;
        }
        return null;
    }
}
