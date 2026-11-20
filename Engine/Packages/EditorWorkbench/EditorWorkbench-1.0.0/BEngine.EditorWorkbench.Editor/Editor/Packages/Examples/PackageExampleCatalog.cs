using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor;

internal static class PackageExampleCatalog
{
    public static PackageExampleInfo[] Discover(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !EditorCapabilities.GetRequiredService<FileUtility>().DirectoryExists(directory)) return [];
        return EditorCapabilities.GetRequiredService<FileUtility>().EnumerateFiles(directory, "*.bpackage", SearchOption.TopDirectoryOnly)
            .OrderBy(EditorCapabilities.GetRequiredService<PathUtility>().GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(Read)
            .ToArray();
    }

    public static string? FindCoreExamplesDirectory()
    {
        var installed = EditorCapabilities.GetRequiredService<PathUtility>().Combine(EditorCapabilities.GetRequiredService<PlatformUtility>().BaseDirectory, EditorResource.DirectoryName, "Examples");
        if (EditorCapabilities.GetRequiredService<FileUtility>().DirectoryExists(installed)) return installed;

        var current = EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(EditorCapabilities.GetRequiredService<PlatformUtility>().BaseDirectory);
        while (current is not null)
        {
            foreach (var sourceRoot in new[]
                     {
                         EditorCapabilities.GetRequiredService<PathUtility>().Combine(current, "Engine", "src"),
                         EditorCapabilities.GetRequiredService<PathUtility>().Combine(current, "src")
                     })
            {
                var source = EditorCapabilities.GetRequiredService<PathUtility>().Combine(
                    sourceRoot, "Core", EditorResource.DirectoryName, "Examples");
                if (EditorCapabilities.GetRequiredService<FileUtility>().DirectoryExists(source) &&
                    EditorCapabilities.GetRequiredService<FileUtility>().FileExists(
                        EditorCapabilities.GetRequiredService<PathUtility>().Combine(sourceRoot, "BEngine.sln")))
                    return source;
            }
            current = EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(current);
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
