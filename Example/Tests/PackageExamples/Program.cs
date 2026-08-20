using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

var repositoryRoot = FindRepositoryRoot();
ValidateDefaultProject(repositoryRoot);
ValidateArchives(repositoryRoot);

Console.WriteLine(
    "PACKAGE_EXAMPLES_OK|default-project-empty,on-demand-import,partial-repair,reimport-preserve," +
    "reimport-overwrite," +
    "no-duplicate-import,readme,scene,asmdef,tools-menuitems");

static void ValidateDefaultProject(string repositoryRoot)
{
    var projectRoot = Path.Combine(repositoryRoot, "Example");
    Require(!Directory.Exists(Path.Combine(projectRoot, "Assets", "Examples")),
        "The default Example project must not contain imported examples.");

    var packages = Document.Load<PackageManifestDocument>(Path.Combine(projectRoot, "Packages", "manifest.yaml"));
    Require(packages.Packages.Count == 0, "The default Example project must not enable extension packages.");

    var project = Document.Load<ProjectDocument>(Path.Combine(projectRoot, "Project.yaml"));
    Require(!project.StartupScene.Contains("Examples", StringComparison.OrdinalIgnoreCase),
        "The default startup scene must not come from an imported example.");
}

static void ValidateArchives(string repositoryRoot)
{
    var modules = new[]
    {
        "Core", "Animation", "Navigation2D", "Physics2D", "PropertyAttributes", "TiledMap", "UIElements"
    };
    var temporaryRoot = Path.Combine(Path.GetTempPath(), $"BEngine.PackageExamples.{Guid.NewGuid():N}");
    try
    {
        foreach (var module in modules)
        {
            var examplesDirectory = Path.Combine(repositoryRoot, "src", module, "EditorResources", "Examples");
            var archives = Directory.EnumerateFiles(examplesDirectory, "*.bpackage", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Require(archives.Length >= 2, $"{module} must provide at least two importable examples.");

            foreach (var archive in archives)
            {
                var manifest = BPackageArchive.ReadManifest(archive);
                var exampleName = Path.GetFileNameWithoutExtension(archive);
                var importPath = $"Examples/{exampleName}";
                Require(manifest.DefaultImportPath.Equals(importPath, StringComparison.Ordinal),
                    $"{Path.GetFileName(archive)} must import into {importPath}.");
                Require(manifest.Entries.Any(entry => entry.RelativePath.EndsWith("Readme.md", StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(archive)} is missing its usage guide.");
                Require(manifest.Entries.Any(entry => entry.RelativePath.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(archive)} is missing a runnable scene.");
                Require(manifest.Entries.Any(entry => entry.RelativePath.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(archive)} is missing an assembly definition.");
                Require(manifest.Entries.Where(entry => !entry.IsDirectory).All(entry =>
                        !entry.RelativePath.Contains('/') ||
                        entry.RelativePath.StartsWith("Editor/", StringComparison.Ordinal) ||
                        entry.RelativePath.StartsWith("runtime/", StringComparison.Ordinal) ||
                        entry.RelativePath.StartsWith("res/", StringComparison.Ordinal)),
                    $"{Path.GetFileName(archive)} contains files outside Editor, runtime or res.");
                Require(manifest.Entries.Any(entry => entry.RelativePath.Equals("runtime", StringComparison.Ordinal)),
                    $"{Path.GetFileName(archive)} is missing its runtime directory.");
                Require(manifest.Entries.Any(entry => entry.RelativePath.Equals("res", StringComparison.Ordinal)),
                    $"{Path.GetFileName(archive)} is missing its res directory.");

                var importRoot = Path.Combine(temporaryRoot, module, Path.GetFileNameWithoutExtension(archive));
                var workspace = ProjectWorkspaceFactory.Create(importRoot, "Package Example");
                var installationId = $"tests.package-example.{module}.{exampleName}";
                var result = BPackageArchive.ImportPackage(workspace, archive,
                    new BPackageImportOptions
                    {
                        DestinationDirectory = importPath,
                        InstallationId = installationId,
                        Mode = BPackageImportMode.Import
                    });
                Require(result.ImportedPaths.Count > 0, $"{Path.GetFileName(archive)} imported no assets.");
                Require(result.Receipt?.InstallationId == installationId,
                    $"{Path.GetFileName(archive)} did not persist an import receipt.");
                Require(BPackageArchive.ReadImportReceipt(workspace, installationId)?.DestinationDirectory ==
                        importPath,
                    $"{Path.GetFileName(archive)} import receipt lost its destination.");
                Require(BPackageArchive.GetImportStatus(workspace, archive, installationId).State ==
                        BPackageImportState.Installed,
                    $"{Path.GetFileName(archive)} was not reported as installed after Import.");
                foreach (var entry in manifest.Entries.Where(entry => !entry.IsDirectory))
                {
                    var importedPath = Path.Combine(workspace.AssetsPath,
                        importPath.Replace('/', Path.DirectorySeparatorChar),
                        entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Require(File.Exists(importedPath), $"Imported asset is missing: {entry.RelativePath}");
                }

                foreach (var sourcePath in Directory.EnumerateFiles(workspace.AssetsPath, "*.cs", SearchOption.AllDirectories))
                {
                    var source = File.ReadAllText(sourcePath);
                    foreach (Match match in Regex.Matches(source, "\\[MenuItem\\(\\\"(?<path>[^\\\"]+)\\\""))
                    {
                        var path = match.Groups["path"].Value;
                        Require(path.StartsWith("Tools/", StringComparison.Ordinal),
                            $"Example MenuItem must be under Tools: {path} ({sourcePath})");
                    }
                }

                ValidateReimport(workspace, archive, manifest, importPath, exampleName, installationId);
            }
        }
    }
    finally
    {
        try
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
        catch (IOException)
        {
            // Script compilation or antivirus scanners may briefly retain generated files.
        }
    }
}

static void ValidateReimport(
    ProjectWorkspace workspace,
    string archive,
    BPackageManifest manifest,
    string importPath,
    string exampleName,
    string installationId)
{
    var destination = Path.Combine(workspace.AssetsPath,
        importPath.Replace('/', Path.DirectorySeparatorChar));
    var initialFiles = SnapshotFiles(destination);
    Require(initialFiles.Count > 0, $"{Path.GetFileName(archive)} produced an empty example directory.");

    var contentEntry = manifest.Entries.First(entry => !entry.IsDirectory &&
        !entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
    var contentPath = Path.Combine(destination,
        contentEntry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    File.Delete(contentPath);
    var partialStatus = BPackageArchive.GetImportStatus(workspace, archive, installationId);
    Require(partialStatus.State == BPackageImportState.Partial &&
            partialStatus.MissingPaths.Contains(
                $"Assets/{importPath}/{contentEntry.RelativePath}", StringComparer.OrdinalIgnoreCase),
        $"{Path.GetFileName(archive)} did not detect its missing imported asset.");
    var repair = BPackageArchive.ImportPackage(workspace, archive,
        new BPackageImportOptions
        {
            DestinationDirectory = importPath,
            InstallationId = installationId,
            Mode = BPackageImportMode.Reimport
        });
    Require(File.Exists(contentPath) && repair.ImportedPaths.Contains(
            $"Assets/{importPath}/{contentEntry.RelativePath}", StringComparer.OrdinalIgnoreCase),
        $"{Path.GetFileName(archive)} did not repair its missing imported asset.");

    File.WriteAllText(contentPath, "locally modified before package-manager reimport");
    var localFile = Path.Combine(destination, "LocalNotes.keep");
    File.WriteAllText(localFile, "not owned by the example archive");
    var modifiedStatus = BPackageArchive.GetImportStatus(workspace, archive, installationId);
    Require(modifiedStatus.State == BPackageImportState.Modified &&
            modifiedStatus.ModifiedPaths.Contains(
                $"Assets/{importPath}/{contentEntry.RelativePath}", StringComparer.OrdinalIgnoreCase),
        $"{Path.GetFileName(archive)} did not detect its modified imported asset.");

    var safeReimport = BPackageArchive.ImportPackage(workspace, archive,
        new BPackageImportOptions
        {
            DestinationDirectory = importPath,
            InstallationId = installationId,
            Mode = BPackageImportMode.Reimport
        });
    Require(safeReimport.PreservedModifiedPaths.Contains(
            $"Assets/{importPath}/{contentEntry.RelativePath}", StringComparer.OrdinalIgnoreCase),
        $"{Path.GetFileName(archive)} did not preserve a modified asset by default.");
    Require(File.ReadAllText(contentPath) == "locally modified before package-manager reimport",
        $"{Path.GetFileName(archive)} silently overwrote a modified asset during safe Reimport.");
    Require(BPackageArchive.GetImportStatus(workspace, archive, installationId).State ==
            BPackageImportState.Modified,
        $"{Path.GetFileName(archive)} lost its Modified state after preserving a user edit.");

    var reimport = BPackageArchive.ImportPackage(workspace, archive,
        new BPackageImportOptions
        {
            DestinationDirectory = importPath,
            InstallationId = installationId,
            Mode = BPackageImportMode.Reimport,
            ConflictPolicy = BPackageConflictPolicy.Overwrite,
            ModifiedFilePolicy = BPackageModifiedFilePolicy.Overwrite
        });
    Require(reimport.SkippedPaths.Count == 0,
        $"{Path.GetFileName(archive)} skipped archive-owned files during Reimport.");
    Require(reimport.ImportedPaths.Count == reimport.ImportedPaths.Distinct(
                StringComparer.OrdinalIgnoreCase).Count(),
        $"{Path.GetFileName(archive)} returned duplicate imported paths during Reimport.");
    Require(reimport.PreservedModifiedPaths.Count == 0 && reimport.Receipt is not null,
        $"{Path.GetFileName(archive)} did not apply explicit Overwrite during Reimport.");

    var reimportedFiles = SnapshotFiles(destination, localFile, localFile + ".meta");
    Require(initialFiles.Count == reimportedFiles.Count && initialFiles.All(pair =>
            reimportedFiles.TryGetValue(pair.Key, out var hash) && hash == pair.Value),
        $"{Path.GetFileName(archive)} Reimport did not restore the archive exactly.");
    Require(File.ReadAllText(localFile) == "not owned by the example archive",
        $"{Path.GetFileName(archive)} Reimport damaged a user-owned file.");
    Require(BPackageArchive.GetImportStatus(workspace, archive, installationId).State ==
            BPackageImportState.Installed,
        $"{Path.GetFileName(archive)} did not return to Installed after Reimport.");

    var examplesRoot = Path.Combine(workspace.AssetsPath, "Examples");
    var matchingDirectories = Directory.EnumerateDirectories(examplesRoot, "*", SearchOption.TopDirectoryOnly)
        .Where(path => Path.GetFileName(path).StartsWith(exampleName, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    Require(matchingDirectories.Length == 1 && Path.GetFullPath(matchingDirectories[0]).Equals(
            Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase),
        $"{Path.GetFileName(archive)} Reimport created a duplicate example directory.");
}

static IReadOnlyDictionary<string, string> SnapshotFiles(string root, params string[] excludedPaths)
{
    var excluded = excludedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !excluded.Contains(Path.GetFullPath(path)))
        .ToDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.OrdinalIgnoreCase);
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
         directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
            return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
