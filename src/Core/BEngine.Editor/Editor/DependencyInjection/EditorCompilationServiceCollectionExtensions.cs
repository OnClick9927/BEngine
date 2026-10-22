using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

internal static class EditorCompilationServiceCollectionExtensions
{
    internal static IServiceCollection AddBEngineEditorCompilation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPackageSourceCompiler, PackageSourceCompilerService>();
        services.TryAddSingleton<IProjectScriptCompiler, ProjectScriptCompilerService>();
        return services;
    }
}
