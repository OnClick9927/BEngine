using BEngine.Build;
using BEngine.Hosting;
using BEngine.ProjectSystem;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

/// <summary>Composes neutral window and rendering options without creating a platform window.</summary>
public static class HostPresentationConfiguration
{
    public static HostPresentationOptions Create(RuntimeProjectData project, RuntimeSettingsData settings,
        IServiceProvider services)
    {
        RuntimeConfiguration.Validate(project);
        RuntimeConfiguration.Validate(settings);
        var target = BuildTargetManifestSerializer.LoadCurrent(null,
            services.GetRequiredService<FileUtility>(), services.GetRequiredService<PathUtility>(),
            services.GetRequiredService<PlatformUtility>()).ToDescriptor();
        var window = project.Window;
        return new(new(window.Title, window.Width, window.Height, window.VSync), target,
            GraphicsBackendDefaults.Parse(settings.GraphicsBackend));
    }
}
