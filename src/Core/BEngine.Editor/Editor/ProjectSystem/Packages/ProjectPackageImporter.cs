using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

/// <summary>Copies project packages once. Imported source belongs to the project and survives reloads.</summary>
internal static class ProjectPackageImporter
{
    internal static ImportTransaction ImportEnabled(
        ProjectWorkspace workspace, BPackageCatalog catalog, IEnumerable<PackageReferenceDocument> references)
    {
        var transaction = new ImportTransaction();
        try
        {
            ImportEnabled(workspace, catalog, references, transaction);
            return transaction;
        }
        catch
        {
            transaction.Dispose();
            throw;
        }
    }

    private static void ImportEnabled(ProjectWorkspace workspace, BPackageCatalog catalog,
        IEnumerable<PackageReferenceDocument> references, ImportTransaction transaction)
    {
        var fs = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var result = transaction.Definitions;
        foreach (var reference in references.Where(item => item.Enabled && item.Type == "project"))
        {
            var id = reference.Id;
            var folder = PackageDefinitionLoader.Normalize(new PackageDefinitionDocument
            {
                Id = id, Type = "project", ProjectDirectory = reference.ProjectDirectory
            }, workspace.PackageManifestPath).ProjectDirectory;
            var target = paths.Combine(workspace.ThirdPartyPath, folder);
            var installed = paths.Combine(target, "package.yaml");
            if (fs.FileExists(installed))
            {
                var document = PackageDefinitionLoader.Load(installed);
                if (document.Type != "project" || !document.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Project package destination belongs to another package: {target}.");
                result.Add(id, new BPackageDefinition(installed, document));
                continue;
            }
            if (fs.DirectoryExists(target) || fs.FileExists(target))
                throw new IOException($"Project package destination already exists: {target}. Existing project files were preserved.");
            if (!catalog.TryGet(id, out var definition) || definition.Document.Type != "project")
                throw new FileNotFoundException($"Project package '{id}' is missing from both Assets/3rd and the engine catalog.");
            fs.CreateDirectory(workspace.ThirdPartyPath);
            fs.CreateDirectory(workspace.TempPath);
            var staging = paths.Combine(workspace.TempPath, $"project-package-{Guid.NewGuid():N}");
            var source = paths.GetDirectoryName(definition.Path)!;
            try
            {
                foreach (var file in fs.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = paths.GetRelativePath(source, file);
                    if (relative.Split('/', '\\').Any(segment => segment is "bin" or "obj" or ".git")) continue;
                    var destination = paths.Combine(staging, relative);
                    fs.CreateDirectory(paths.GetDirectoryName(destination)!);
                    fs.CopyFile(file, destination, false);
                }
                fs.MoveDirectory(staging, target);
                transaction.CreatedDirectories.Add(target);
            }
            finally
            {
                if (fs.DirectoryExists(staging)) fs.DeleteDirectory(staging, true);
            }
            result.Add(id, new BPackageDefinition(installed, PackageDefinitionLoader.Load(installed)));
        }
    }

    internal sealed class ImportTransaction : IDisposable
    {
        internal Dictionary<string, BPackageDefinition> Definitions { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        internal List<string> CreatedDirectories { get; } = [];
        private bool _committed;

        internal void Commit() => _committed = true;

        public void Dispose()
        {
            if (_committed) return;
            foreach (var directory in CreatedDirectories.AsEnumerable().Reverse())
            {
                // These destinations did not exist before this import. Never remove an already
                // installed project package: its files may contain the project's own edits.
                if (EditorHostServices.FileSystem.DirectoryExists(directory))
                    EditorHostServices.FileSystem.DeleteDirectory(directory, true);
            }
            CreatedDirectories.Clear();
        }
    }
}
