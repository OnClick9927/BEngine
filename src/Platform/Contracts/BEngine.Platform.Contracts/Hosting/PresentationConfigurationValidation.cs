using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.ProjectSystem;

namespace BEngine.Hosting;

internal sealed class PresentationConfigurationValidation : IRuntimeConfigurationExtension
{
    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<IRuntimeConfigurationExtension>(new PresentationConfigurationValidation());
    public void Validate(RuntimeProjectData project)
    {
        if (project.Window is null || project.Window.Width < 320 || project.Window.Height < 200)
            throw new InvalidDataException("Runtime window size is too small.");
    }
    public void Validate(RuntimeSettingsData settings)
    {
        if (settings.DefaultScreenWidth < 320 || settings.DefaultScreenHeight < 200)
            throw new InvalidDataException("Default screen resolution is too small.");
    }
}
