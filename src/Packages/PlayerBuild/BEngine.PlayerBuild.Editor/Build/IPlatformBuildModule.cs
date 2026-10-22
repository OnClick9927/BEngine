using System.Reflection;
using BEngine.Platform;

namespace BEngine.Editor;

public sealed record PlatformBuildServices(
    ProcessUtility ProcessRunner,
    ApplicationPathUtility ApplicationPaths,
    FileUtility FileSystem,
    PathUtility Paths,
    PlatformUtility Platform,
    PlatformPlayerBuildCapabilities Capabilities);

public interface IPlatformBuildModule
{
    IPlayerBuildTargetProvider CreateProvider(PlatformBuildServices services);
}

internal static class PlatformBuildProviderLoader
{
    internal static IEnumerable<IPlayerBuildTargetProvider> Load(PlatformBuildServices services)
    {
        var assemblies = services.FileSystem.EnumerateFiles(
            services.Platform.BaseDirectory, "BEngine.*.Build.dll", SearchOption.TopDirectoryOnly).ToList();
        var editorOutput = services.Paths.GetDirectoryName(typeof(EditorWindow).Assembly.Location);
        var editorAssemblyRoot = editorOutput is null ? null : services.Paths.GetDirectoryName(editorOutput);
        if (editorAssemblyRoot is not null && services.Paths.GetFileName(editorAssemblyRoot) == "BEngine.Editor")
        {
            var artifactRoot = services.Paths.GetDirectoryName(editorAssemblyRoot)!;
            foreach (var project in services.FileSystem.EnumerateDirectories(artifactRoot, "BEngine.*.Build"))
            {
                var output = services.Paths.Combine(project, services.Paths.GetFileName(editorOutput!));
                if (services.FileSystem.DirectoryExists(output))
                    assemblies.AddRange(services.FileSystem.EnumerateFiles(output, "BEngine.*.Build.dll"));
            }
        }
        foreach (var path in assemblies.DistinctBy(services.Paths.GetFileName).Order(StringComparer.Ordinal))
        {
            var assembly = Assembly.LoadFrom(path);
            foreach (var type in assembly.GetTypes().Where(type =>
                         type is { IsClass: true, IsAbstract: false, IsPublic: true } &&
                         typeof(IPlatformBuildModule).IsAssignableFrom(type)).OrderBy(type => type.FullName))
                yield return ((IPlatformBuildModule)Activator.CreateInstance(type)!).CreateProvider(services);
        }
    }
}
