using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

public static class EditorCompilationServiceCollectionExtensions
{
    /// <summary>Registers the engine's package and project compilation mechanisms for an editor host.</summary>
    public static IServiceCollection AddBEngineEditorCompilation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EditorServiceRegistrationCatalog.Register(services);
        services.TryAddSingleton<IPackageSourceCompiler, PackageSourceCompilerService>();
        services.TryAddSingleton<IProjectScriptCompiler, ProjectScriptCompilerService>();
        services.TryAddSingleton<IProjectPackageManagerFactory, ProjectPackageManagerFactory>();
        services.TryAddSingleton<IEditorCommandLineRunner, EditorPackageCommandLine>();
        return services;
    }
}
