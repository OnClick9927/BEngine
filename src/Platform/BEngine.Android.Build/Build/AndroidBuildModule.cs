using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed class AndroidBuildModule : IPlatformBuildModule
{
    public IPlayerBuildTargetProvider CreateProvider(PlatformBuildServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        PlatformAssetImportDefaults.RegisterMobile(BuildTargetPlatform.Android);
        foreach (var targetId in new[] { "android-arm64", "android-x64" })
        if (!services.Capabilities.TryGetRenderer(targetId, out _))
        {
            var hostProject = FindHostProject(services);
            if (hostProject is not null)
                services.Capabilities.RegisterRenderer(new PlatformPlayerRendererCapability(
                    "bengine.android.native", targetId,
                    targetId == "android-x64" ? [GraphicsBackend.OpenGLES] : [GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES], hostProject,
                    SupportsHybridAotInterpreter: true));
        }
        return new DotNetAndroidBuildTargetProvider(services.ProcessRunner, services.ApplicationPaths,
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
                         services.Paths.Combine(directory, "src", "Platform", "Hosts", "Android",
                             "BEngine.Player.AndroidHost.csproj"),
                         services.Paths.Combine(directory, "Engine", "src", "Platform", "Hosts", "Android",
                             "BEngine.Player.AndroidHost.csproj"),
                         services.Paths.Combine(directory, "BuildHosts", "Android",
                             "BEngine.Player.AndroidHost.csproj")
                     })
                if (services.FileSystem.FileExists(candidate)) return candidate;
        }
        return null;
    }
}
