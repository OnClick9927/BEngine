using BEngine.ProjectSystem;

namespace BEngine.Editor;

internal static class PackageDocumentationCatalog
{
    private const string DocumentationFileName = "index.html";

    public static string? FindCoreDocumentation()
    {
        var installed = FindAtPackageRoot(EditorHostServices.Platform.BaseDirectory);
        if (installed is not null) return installed;

        for (var current = EditorHostServices.Paths.GetFullPath(EditorHostServices.Platform.BaseDirectory);
             current is not null;
             current = EditorHostServices.Paths.GetDirectoryName(current))
        {
            foreach (var sourceRoot in new[]
                     {
                         EditorHostServices.Paths.Combine(current, "Engine", "src"),
                         EditorHostServices.Paths.Combine(current, "src")
                     })
            {
                var source = FindAtPackageRoot(EditorHostServices.Paths.Combine(sourceRoot, "Core"));
                if (source is not null && EditorHostServices.FileSystem.FileExists(
                        EditorHostServices.Paths.Combine(sourceRoot, "BEngine.sln")))
                    return source;
            }
        }
        return null;
    }

    public static string? FindForPackage(BPackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var packageRoot = EditorHostServices.Paths.GetDirectoryName(package.Path);
        return string.IsNullOrWhiteSpace(packageRoot) ? null : FindAtPackageRoot(packageRoot);
    }

    public static string? FindAtPackageRoot(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var path = EditorHostServices.Paths.Combine(packageRoot, EditorResource.DirectoryName, "Doc", DocumentationFileName);
        return EditorHostServices.FileSystem.FileExists(path) ? EditorHostServices.Paths.GetFullPath(path) : null;
    }
}
