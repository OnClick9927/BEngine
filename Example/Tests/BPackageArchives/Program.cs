using System.Security.Cryptography;
using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

RunArchiveContract();
ValidatePublishedExamples(FindRepositoryRoot());
Console.WriteLine("BPACKAGE_ARCHIVES_OK|deterministic,manifest,meta,import,conflicts,overwrite,published-examples");

static void RunArchiveContract()
{
    var root = Path.Combine(Path.GetTempPath(), $"BEngine.BPackage.{Guid.NewGuid():N}");
    try
    {
        var source = ProjectWorkspaceFactory.Create(Path.Combine(root, "Source"), "Source");
        var target = ProjectWorkspaceFactory.Create(Path.Combine(root, "Target"), "Target");
        var sampleDirectory = Path.Combine(source.AssetsPath, "Samples");
        Directory.CreateDirectory(sampleDirectory);
        File.WriteAllText(Path.Combine(sampleDirectory, "Greeting.txt"), "hello bpackage");
        new BEngine.ProjectSystem.Editor.AssetDatabase(source).Refresh();

        var first = Path.Combine(root, "First.bpackage");
        var second = Path.Combine(root, "Second.bpackage");
        var options = new BPackageExportOptions
        {
            Name = "Archive Contract",
            Description = "BPackage archive round-trip contract.",
            DefaultImportPath = "Examples/ArchiveContract",
            IncludeMetaFiles = true
        };
        var manifest = BPackageArchive.ExportPackage(source, ["Assets/Samples"], first, options);
        _ = BPackageArchive.ExportPackage(source, ["Assets/Samples"], second, options);

        Require(manifest.Format == "BEngine.BPackage" && manifest.Version == 1, "manifest format");
        Require(manifest.DefaultImportPath == "Examples/ArchiveContract", "default import path");
        Require(manifest.Entries.Any(entry => entry.RelativePath == "Samples/Greeting.txt"), "asset entry");
        Require(manifest.Entries.Any(entry => entry.RelativePath == "Samples/Greeting.txt.meta"), "meta entry");
        Require(SHA256.HashData(File.ReadAllBytes(first)).SequenceEqual(
            SHA256.HashData(File.ReadAllBytes(second))), "deterministic output");

        var read = BPackageArchive.ReadManifest(first);
        Require(read.Entries.Count == manifest.Entries.Count, "manifest inspection");
        var imported = BPackageArchive.ImportPackage(target, first);
        var importedPath = Path.Combine(target.AssetsPath, "Examples", "ArchiveContract", "Samples",
            "Greeting.txt");
        Require(File.ReadAllText(importedPath) == "hello bpackage", "imported content");
        Require(imported.ImportedPaths.Count > 0, "import result");

        var conflicted = false;
        try { _ = BPackageArchive.ImportPackage(target, first); }
        catch (IOException) { conflicted = true; }
        Require(conflicted, "safe conflict default");

        File.WriteAllText(importedPath, "changed");
        _ = BPackageArchive.ImportPackage(target, first,
            new BPackageImportOptions { ConflictPolicy = BPackageConflictPolicy.Overwrite });
        Require(File.ReadAllText(importedPath) == "hello bpackage", "explicit overwrite");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void ValidatePublishedExamples(string repositoryRoot)
{
    var definitions = new (string Module, string Archive, string Scene)[]
    {
        ("Core", "src/Core/EditorResources/Examples/CoreGettingStarted.bpackage", "Core.scene.yaml"),
        ("Core", "src/Core/EditorResources/Examples/EcsSystems.bpackage", "EcsSystems.scene.yaml"),
        ("Animation", "src/Animation/EditorResources/Examples/AnimationGettingStarted.bpackage",
            "Animation.scene.yaml"),
        ("Animation", "src/Animation/EditorResources/Examples/StateMachine.bpackage",
            "StateMachine.scene.yaml"),
        ("Navigation2D", "src/Navigation2D/EditorResources/Examples/NavigationSurfaceAndAgent.bpackage",
            "Navigation.scene.yaml"),
        ("Navigation2D", "src/Navigation2D/EditorResources/Examples/DynamicRebake.bpackage",
            "DynamicRebake.scene.yaml"),
        ("Physics2D", "src/Physics2D/EditorResources/Examples/RigidbodyAndQueries.bpackage",
            "Physics2D.scene.yaml"),
        ("Physics2D", "src/Physics2D/EditorResources/Examples/TriggersAndQueries.bpackage",
            "TriggersAndQueries.scene.yaml"),
        ("PropertyAttributes",
            "src/PropertyAttributes/EditorResources/Examples/InspectorAttributesAndDrawer.bpackage",
            "PropertyAttributes.scene.yaml"),
        ("PropertyAttributes", "src/PropertyAttributes/EditorResources/Examples/AttributesGallery.bpackage",
            "AttributesGallery.scene.yaml"),
        ("TiledMap", "src/TiledMap/EditorResources/Examples/RuntimePainting.bpackage",
            "RuntimePainting.scene.yaml"),
        ("TiledMap", "src/TiledMap/EditorResources/Examples/AtlasPalette.bpackage",
            "AtlasPalette.scene.yaml"),
        ("UIElements", "src/UIElements/EditorResources/Examples/RuntimeHud.bpackage",
            "UIElements.scene.yaml"),
        ("UIElements", "src/UIElements/EditorResources/Examples/ControlsGallery.bpackage",
            "ControlsGallery.scene.yaml")
    };
    var root = Path.Combine(Path.GetTempPath(), $"BEngine.PublishedExamples.{Guid.NewGuid():N}");
    try
    {
        foreach (var definition in definitions)
        {
            Console.WriteLine($"BPACKAGE_EXAMPLE_VALIDATE|{definition.Module}");
            var archivePath = Path.Combine(repositoryRoot,
                definition.Archive.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(archivePath), $"published archive {definition.Module}");
            var manifest = BPackageArchive.ReadManifest(archivePath);
            var exampleName = Path.GetFileNameWithoutExtension(definition.Archive);
            var importPath = $"Examples/{exampleName}";
            Require(manifest.DefaultImportPath.Equals(importPath, StringComparison.Ordinal),
                $"default import path {definition.Module}");
            Require(manifest.Entries.Where(entry => !entry.IsDirectory).All(entry =>
                    !entry.RelativePath.Contains('/') ||
                    entry.RelativePath.StartsWith("Editor/", StringComparison.Ordinal) ||
                    entry.RelativePath.StartsWith("runtime/", StringComparison.Ordinal) ||
                    entry.RelativePath.StartsWith("res/", StringComparison.Ordinal)),
                $"isolated paths {definition.Module}");
            Require(manifest.Entries.Any(entry => entry.RelativePath == "Readme.md"),
                $"README {definition.Module}");
            Require(manifest.Entries.Any(entry => entry.RelativePath == $"res/{definition.Scene}"),
                $"scene {definition.Module}");
            Require(manifest.Entries.Any(entry => entry.RelativePath.EndsWith(".asmdef.yaml",
                StringComparison.OrdinalIgnoreCase)), $"asmdef {definition.Module}");
            Require(manifest.Entries.Where(entry => !entry.IsDirectory &&
                    !entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .All(entry => manifest.Entries.Any(meta =>
                    meta.RelativePath.Equals(entry.RelativePath + ".meta", StringComparison.OrdinalIgnoreCase))),
                $"meta coverage {definition.Module}");

            var workspace = ProjectWorkspaceFactory.Create(
                Path.Combine(root, definition.Module, exampleName), $"{definition.Module} {exampleName}");
            _ = BPackageArchive.ImportPackage(workspace, archivePath,
                new BPackageImportOptions { DestinationDirectory = importPath });
            Require(File.Exists(Path.Combine(workspace.AssetsPath,
                    importPath.Replace('/', Path.DirectorySeparatorChar), "res", definition.Scene)),
                $"individual import {definition.Module}");
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
         directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
            return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
}

static void Require(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException($"BPackage contract failed: {scenario}.");
}
