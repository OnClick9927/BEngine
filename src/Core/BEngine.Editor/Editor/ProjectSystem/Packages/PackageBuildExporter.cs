using BEngine.Serialization;
using BEngine.Documents;
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
        var destination = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(destination);
        var enabledIds = enabledPackages.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = definitions.Where(item => enabledIds.Contains(item.Document.Id) &&
                                                  item.Document.Runtime is not null)
            .OrderBy(item => item.Document.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var staging = Path.Combine(destination, $"Packages.__staging_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            for (var index = 0; index < selected.Length; index++)
            {
                var definition = selected[index];
                EditorCallbackDispatcher.Invoke(progress, index, selected.Length,
                    definition.Document.DisplayName, "PackageBuildExporter.progress");
                var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(definition.Path))!;
                var packageFolder = Path.GetFileName(sourceRoot);
                var targetRoot = Path.Combine(staging, packageFolder);
                Directory.CreateDirectory(targetRoot);
                File.Copy(definition.Path, Path.Combine(targetRoot, "package.yaml"), true);
                CopyOptionalDirectory(Path.Combine(sourceRoot, "Resources"), Path.Combine(targetRoot, "Resources"));
                if (definition.Document.Runtime is { } runtime)
                {
                    var assemblyPath = ScriptAssemblyStore.ResolveCurrentPath(workspace, runtime.Assembly) ??
                                       throw new FileNotFoundException(
                                           $"Package '{definition.Document.Id}' has not produced runtime assembly " +
                                           $"'{runtime.Assembly}' for this project.",
                                           ScriptAssemblyStore.GetReferencePath(workspace, runtime.Assembly));
                    CopyRuntimeArtifacts(Path.GetDirectoryName(assemblyPath)!, targetRoot);
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
            }.Save(Path.Combine(staging, "manifest.yaml"));
            EditorCallbackDispatcher.Invoke(progress, selected.Length, selected.Length,
                "完成", "PackageBuildExporter.progress");

            var packagesPath = Path.Combine(destination, "Packages");
            var backup = Path.Combine(destination, $"Packages.__backup_{Guid.NewGuid():N}");
            if (Directory.Exists(packagesPath)) Directory.Move(packagesPath, backup);
            try
            {
                Directory.Move(staging, packagesPath);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            catch
            {
                if (!Directory.Exists(packagesPath) && Directory.Exists(backup))
                    Directory.Move(backup, packagesPath);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static void CopyOptionalDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CopyRuntimeArtifacts(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
            CopyOptionalDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
