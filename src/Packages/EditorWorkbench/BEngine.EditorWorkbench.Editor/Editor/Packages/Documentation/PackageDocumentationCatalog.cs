using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

internal static class PackageDocumentationCatalog
{
    private const string DocumentationFileName = "index.html";

    public static string? FindCoreDocumentation()
    {
        var installed = FindAtPackageRoot(EditorCapabilities.GetRequiredService<PlatformUtility>().BaseDirectory);
        if (installed is not null) return installed;

        for (var current = EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(EditorCapabilities.GetRequiredService<PlatformUtility>().BaseDirectory);
             current is not null;
             current = EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(current))
        {
            foreach (var sourceRoot in new[]
                     {
                         EditorCapabilities.GetRequiredService<PathUtility>().Combine(current, "Engine", "src"),
                         EditorCapabilities.GetRequiredService<PathUtility>().Combine(current, "src")
                     })
            {
                var source = FindAtPackageRoot(EditorCapabilities.GetRequiredService<PathUtility>().Combine(sourceRoot, "Core"));
                if (source is not null && EditorCapabilities.GetRequiredService<FileUtility>().FileExists(
                        EditorCapabilities.GetRequiredService<PathUtility>().Combine(sourceRoot, "BEngine.sln")))
                    return source;
            }
        }
        return null;
    }

    public static string? FindForPackage(BPackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var packageRoot = EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(package.Path);
        return string.IsNullOrWhiteSpace(packageRoot) ? null : FindAtPackageRoot(packageRoot);
    }

    public static string? FindAtPackageRoot(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var path = EditorCapabilities.GetRequiredService<PathUtility>().Combine(packageRoot, EditorResource.DirectoryName, "Doc", DocumentationFileName);
        return EditorCapabilities.GetRequiredService<FileUtility>().FileExists(path) ? EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(path) : null;
    }
}
