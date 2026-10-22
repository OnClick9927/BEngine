using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed class WebBuildModule : IPlatformBuildModule
{
    public IPlayerBuildTargetProvider CreateProvider(PlatformBuildServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        const string targetId = "web-wasm";
        if (!services.Capabilities.TryGetRenderer(targetId, out _))
        {
            var hostProject = FindHostProject(services);
            if (hostProject is not null)
                services.Capabilities.RegisterRenderer(new PlatformPlayerRendererCapability(
                    "bengine.webgl2", targetId, [GraphicsBackend.WebGL], hostProject,
                    SupportsHybridAotInterpreter: true));
        }
        return new DotNetWebAssemblyBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
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
                         services.Paths.Combine(directory, "src", "Platform", "Hosts", "Web",
                             "BEngine.Player.WebHost.csproj"),
                         services.Paths.Combine(directory, "Engine", "src", "Platform", "Hosts", "Web",
                             "BEngine.Player.WebHost.csproj"),
                         services.Paths.Combine(directory, "BuildHosts", "Web",
                             "BEngine.Player.WebHost.csproj")
                     })
                if (services.FileSystem.FileExists(candidate)) return candidate;
        }
        return null;
    }
}
