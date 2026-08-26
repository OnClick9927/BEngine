namespace BEngine.Editor;

internal static class PackageExampleCatalog
{
    public static PackageExampleInfo[] Discover(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.bpackage", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(Read)
            .ToArray();
    }

    public static string? FindCoreExamplesDirectory()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, EditorResource.DirectoryName, "Examples");
        if (Directory.Exists(installed)) return installed;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var source = Path.Combine(current.FullName, "src", "Core", EditorResource.DirectoryName, "Examples");
            if (Directory.Exists(source) && File.Exists(Path.Combine(current.FullName, "src", "BEngine.sln")))
                return source;
            current = current.Parent;
        }
        return null;
    }

    private static PackageExampleInfo Read(string archivePath)
    {
        try
        {
            return new PackageExampleInfo(archivePath, BPackageArchive.ReadManifest(archivePath), string.Empty);
        }
        catch (Exception exception)
        {
            return new PackageExampleInfo(archivePath, null, exception.Message);
        }
    }
}
