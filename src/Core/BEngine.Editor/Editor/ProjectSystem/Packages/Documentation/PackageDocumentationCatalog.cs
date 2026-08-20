using BEngine.ProjectSystem;

namespace BEngine.Editor;

internal static class PackageDocumentationCatalog
{
    private const string DocumentationFileName = "index.html";

    public static string? FindCoreDocumentation()
    {
        var installed = FindAtPackageRoot(AppContext.BaseDirectory);
        if (installed is not null) return installed;

        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null;
             current = current.Parent)
        {
            var sourceRoot = Path.Combine(current.FullName, "src", "Core");
            var source = FindAtPackageRoot(sourceRoot);
            if (source is not null && File.Exists(Path.Combine(current.FullName, "src", "BEngine.sln")))
                return source;
        }
        return null;
    }

    public static string? FindForPackage(BPackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var packageRoot = Path.GetDirectoryName(package.Path);
        return string.IsNullOrWhiteSpace(packageRoot) ? null : FindAtPackageRoot(packageRoot);
    }

    public static string? FindAtPackageRoot(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var path = Path.Combine(packageRoot, "EditorResources", "Doc", DocumentationFileName);
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }
}
