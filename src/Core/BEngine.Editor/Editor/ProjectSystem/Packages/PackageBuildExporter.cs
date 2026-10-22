using BEngine.Serialization;
using BEngine.Editor.Documents;
using BEngine.Editor;

namespace BEngine.ProjectSystem;

internal static class PackageBuildExporter
{
    internal static void ExportRuntimePackages(
        ProjectWorkspace workspace,
        string outputDirectory,
        IReadOnlyList<PackageReferenceDocument> enabledPackages,
        IReadOnlyList<BPackageDefinition> definitions,
        Action<int, int, string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var destination = EditorHostServices.Paths.GetFullPath(outputDirectory);
        EditorHostServices.FileSystem.CreateDirectory(destination);
        var enabledIds = enabledPackages.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = definitions.Where(item => enabledIds.Contains(item.Document.Id) &&
                                                  item.Document.Runtime is not null)
            .OrderBy(item => item.Document.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var staging = EditorHostServices.Paths.Combine(destination, $"Packages.__staging_{Guid.NewGuid():N}");
        EditorHostServices.FileSystem.CreateDirectory(staging);
        try
        {
            for (var index = 0; index < selected.Length; index++)
            {
                var definition = selected[index];
                EditorCallbackDispatcher.Invoke(progress, index, selected.Length,
                    definition.Document.DisplayName, "PackageBuildExporter.progress");
                var sourceRoot = EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(definition.Path))!;
                var packageFolder = EditorHostServices.Paths.GetFileName(sourceRoot);
                var targetRoot = EditorHostServices.Paths.Combine(staging, packageFolder);
                EditorHostServices.FileSystem.CreateDirectory(targetRoot);
                EditorHostServices.FileSystem.CopyFile(definition.Path, EditorHostServices.Paths.Combine(targetRoot, "package.yaml"), true);
                CopyOptionalDirectory(EditorHostServices.Paths.Combine(sourceRoot, "Resources"), EditorHostServices.Paths.Combine(targetRoot, "Resources"));
                if (definition.Document.Runtime is { } runtime)
                {
                    var assemblyPath = ScriptAssemblyStore.ResolveCurrentPath(workspace, runtime.Assembly) ??
                                       throw new FileNotFoundException(
                                           $"Package '{definition.Document.Id}' has not produced runtime assembly " +
                                           $"'{runtime.Assembly}' for this project.",
                                           ScriptAssemblyStore.GetReferencePath(workspace, runtime.Assembly));
                    CopyRuntimeArtifacts(EditorHostServices.Paths.GetDirectoryName(assemblyPath)!, targetRoot);
                }
            }
            var runtimePackageIds = selected.Select(definition => definition.Document.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            new PackageManifestDocument
            {
                Packages = enabledPackages.Where(package => runtimePackageIds.Contains(package.Id))
                    .Select(package => new PackageReferenceDocument
                    {
                        Id = package.Id,
                        Version = package.Version,
                        Enabled = true
                    }).ToList()
            }.Save(EditorHostServices.Paths.Combine(staging, "manifest.yaml"));
            EditorCallbackDispatcher.Invoke(progress, selected.Length, selected.Length,
                "完成", "PackageBuildExporter.progress");

            var packagesPath = EditorHostServices.Paths.Combine(destination, "Packages");
            var backup = EditorHostServices.Paths.Combine(destination, $"Packages.__backup_{Guid.NewGuid():N}");
            if (EditorHostServices.FileSystem.DirectoryExists(packagesPath)) EditorHostServices.FileSystem.MoveDirectory(packagesPath, backup);
            try
            {
                EditorHostServices.FileSystem.MoveDirectory(staging, packagesPath);
                if (EditorHostServices.FileSystem.DirectoryExists(backup)) EditorHostServices.FileSystem.DeleteDirectory(backup, true);
            }
            catch
            {
                if (!EditorHostServices.FileSystem.DirectoryExists(packagesPath) && EditorHostServices.FileSystem.DirectoryExists(backup))
                    EditorHostServices.FileSystem.MoveDirectory(backup, packagesPath);
                throw;
            }
        }
        finally
        {
            if (EditorHostServices.FileSystem.DirectoryExists(staging)) EditorHostServices.FileSystem.DeleteDirectory(staging, true);
        }
    }

    private static void CopyOptionalDirectory(string source, string destination)
    {
        if (!EditorHostServices.FileSystem.DirectoryExists(source)) return;
        foreach (var directory in EditorHostServices.FileSystem.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.Combine(destination, EditorHostServices.Paths.GetRelativePath(source, directory)));
        foreach (var file in EditorHostServices.FileSystem.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = EditorHostServices.Paths.Combine(destination, EditorHostServices.Paths.GetRelativePath(source, file));
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(target)!);
            EditorHostServices.FileSystem.CopyFile(file, target, true);
        }
    }

    private static void CopyRuntimeArtifacts(string source, string destination)
    {
        EditorHostServices.FileSystem.CreateDirectory(destination);
        foreach (var file in EditorHostServices.FileSystem.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            EditorHostServices.FileSystem.CopyFile(file, EditorHostServices.Paths.Combine(destination, EditorHostServices.Paths.GetFileName(file)), true);
        foreach (var directory in EditorHostServices.FileSystem.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
            CopyOptionalDirectory(directory, EditorHostServices.Paths.Combine(destination, EditorHostServices.Paths.GetFileName(directory)));
    }
}
