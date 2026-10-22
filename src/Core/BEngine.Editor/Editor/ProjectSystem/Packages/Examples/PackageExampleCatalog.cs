namespace BEngine.Editor;

internal static class PackageExampleCatalog
{
    public static PackageExampleInfo[] Discover(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !EditorHostServices.FileSystem.DirectoryExists(directory)) return [];
        return EditorHostServices.FileSystem.EnumerateFiles(directory, "*.bpackage", SearchOption.TopDirectoryOnly)
            .OrderBy(EditorHostServices.Paths.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(Read)
            .ToArray();
    }

    public static string? FindCoreExamplesDirectory()
    {
        var installed = EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, EditorResource.DirectoryName, "Examples");
        if (EditorHostServices.FileSystem.DirectoryExists(installed)) return installed;

        var current = EditorHostServices.Paths.GetFullPath(EditorHostServices.Platform.BaseDirectory);
        while (current is not null)
        {
            foreach (var sourceRoot in new[]
                     {
                         EditorHostServices.Paths.Combine(current, "Engine", "src"),
                         EditorHostServices.Paths.Combine(current, "src")
                     })
            {
                var source = EditorHostServices.Paths.Combine(
                    sourceRoot, "Core", EditorResource.DirectoryName, "Examples");
                if (EditorHostServices.FileSystem.DirectoryExists(source) &&
                    EditorHostServices.FileSystem.FileExists(
                        EditorHostServices.Paths.Combine(sourceRoot, "BEngine.sln")))
                    return source;
            }
            current = EditorHostServices.Paths.GetDirectoryName(current);
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
