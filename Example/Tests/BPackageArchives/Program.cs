using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

if (args is ["--repack-examples", var authoringRoot])
{
    RepackPublishedExamples(FindRepositoryRoot(), authoringRoot);
    return;
}

RunArchiveContract();
ValidatePublishedExamples(FindRepositoryRoot());
Console.WriteLine(
    "BPACKAGE_ARCHIVES_OK|deterministic,manifest,meta,import,conflicts,overwrite," +
    "published-authored-scenes");

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
    var root = Path.Combine(Path.GetTempPath(), $"BEngine.PublishedExamples.{Guid.NewGuid():N}");
    try
    {
        foreach (var definition in PublishedExamples())
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
            Require(manifest.Entries.All(entry => IsPackageExampleEntry(entry.RelativePath)),
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
            ValidateAuthoredExampleContents(archivePath, definition);

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

static void ValidateAuthoredExampleContents(
    string archivePath,
    (string Module, string Archive, string Scene, string RequiredComponent) definition)
{
    using var archive = ZipFile.OpenRead(archivePath);
    var scene = ReadArchiveText(archive, $"Assets/res/{definition.Scene}");
    var readme = ReadArchiveText(archive, "Assets/Readme.md");
    var gameObjectCount = Regex.Matches(scene, @"(?m)^- id:\s").Count;
    Require(gameObjectCount >= 2,
        $"authored scene {definition.Module}/{definition.Scene} has only {gameObjectCount} GameObject.");
    Require(scene.Contains("type: BEngine.Camera2D", StringComparison.Ordinal),
        $"authored scene {definition.Module}/{definition.Scene} has no Camera2D.");
    Require(scene.Contains(definition.RequiredComponent, StringComparison.Ordinal),
        $"authored scene {definition.Module}/{definition.Scene} has no {definition.RequiredComponent}.");
    Require(readme.Length >= 800 && readme.Contains("##", StringComparison.Ordinal),
        $"example guide {definition.Module}/{definition.Scene} is not detailed enough.");
    ValidateProjectAssetReferences(archive, archivePath);

    if (Path.GetFileNameWithoutExtension(archivePath).Equals("AtlasPalette", StringComparison.Ordinal))
    {
        var palette = ReadArchiveText(archive, "Assets/res/AtlasPalette.tilepalette.yaml");
        var atlas = ReadArchiveText(archive, "Assets/res/AtlasPalette.atlas.yaml");
        Require(palette.Contains(
                "atlas: Assets/Examples/AtlasPalette/res/AtlasTiles.png", StringComparison.Ordinal),
            "AtlasPalette does not reference its packed texture.");
        Require(archive.GetEntry("Assets/res/AtlasTiles.png") is not null,
            "AtlasPalette packed texture is missing.");
        Require(Regex.Matches(atlas, @"(?m)^- Assets/Examples/AtlasPalette/res/Sprites/").Count == 5 &&
                Regex.Matches(atlas, @"(?m)^- name:").Count == 5,
            "AtlasPalette TextureAtlas must hold five Sprite references and packed regions.");
    }
}

static void ValidateProjectAssetReferences(ZipArchive archive, string archivePath)
{
    var exampleName = Path.GetFileNameWithoutExtension(archivePath);
    foreach (var entry in archive.Entries.Where(entry =>
                 entry.FullName.StartsWith("Assets/", StringComparison.Ordinal) &&
                 (entry.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                  entry.FullName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))))
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        foreach (Match match in Regex.Matches(text,
                     @"Assets/Examples/(?<example>[A-Za-z0-9._-]+)/(?<path>[A-Za-z0-9_./-]+)"))
        {
            Require(match.Groups["example"].Value.Equals(exampleName, StringComparison.Ordinal),
                $"{exampleName} contains a project asset path for another example: {match.Value}");
            var path = match.Groups["path"].Value.TrimEnd('.');
            if (!Path.HasExtension(path)) continue;
            Require(archive.Entries.Any(candidate => candidate.FullName.TrimEnd('/').Equals(
                        $"Assets/{path}".TrimEnd('/'), StringComparison.OrdinalIgnoreCase)),
                $"{exampleName} references an asset that is absent from the archive: {match.Value}");
        }
    }
}

static string ReadArchiveText(ZipArchive archive, string path)
{
    var entry = archive.GetEntry(path) ??
                throw new InvalidDataException($"Archive entry is missing: {path}");
    using var stream = entry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    return reader.ReadToEnd();
}

static void RepackPublishedExamples(string repositoryRoot, string authoringRoot)
{
    var sourceRoot = Path.GetFullPath(authoringRoot);
    Require(Directory.Exists(sourceRoot), $"Example authoring root does not exist: {sourceRoot}");
    foreach (var definition in PublishedExamples())
    {
        var exampleName = Path.GetFileNameWithoutExtension(definition.Archive);
        var exampleRoot = Path.Combine(sourceRoot, exampleName);
        var assetsPath = Path.Combine(exampleRoot, "Assets");
        Require(Directory.Exists(assetsPath), $"Example authoring Assets directory is missing: {exampleRoot}");
        var allowedRootEntries = new HashSet<string>(
            ["Editor", "runtime", "res", "Readme.md"], StringComparer.Ordinal);
        var selections = Directory.EnumerateFileSystemEntries(assetsPath)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Where(path => allowedRootEntries.Contains(Path.GetFileName(path)))
            .Select(path => $"Assets/{Path.GetFileName(path)}")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Require(selections.Contains("Assets/runtime", StringComparer.Ordinal) &&
                selections.Contains("Assets/res", StringComparer.Ordinal) &&
                selections.Contains("Assets/Readme.md", StringComparer.Ordinal),
            $"Example authoring layout is incomplete: {exampleRoot}");
        var destination = Path.Combine(repositoryRoot,
            definition.Archive.Replace('/', Path.DirectorySeparatorChar));
        var original = BPackageArchive.ReadManifest(destination);
        var projectPath = Path.Combine(exampleRoot, ProjectWorkspace.ProjectFileName);
        if (!File.Exists(projectPath))
            new ProjectDocument { Name = original.Name }.Save(projectPath);
        var workspace = ProjectWorkspace.Open(exampleRoot);
        new BEngine.ProjectSystem.Editor.AssetDatabase(workspace).Refresh();
        _ = BPackageArchive.ExportPackage(workspace, selections, destination,
            new BPackageExportOptions
            {
                Name = original.Name,
                PackageVersion = original.PackageVersion,
                Description = original.Description,
                DefaultImportPath = original.DefaultImportPath,
                IncludeMetaFiles = true
            });
        Console.WriteLine($"BPACKAGE_EXAMPLE_REPACKED|{definition.Module}|{exampleName}");
    }
}

static (string Module, string Archive, string Scene, string RequiredComponent)[] PublishedExamples() =>
[
    ("Core", "src/Core/EditorResources/Examples/CoreGettingStarted.bpackage", "Core.scene.yaml",
        "type: BEngine.SpriteRenderer"),
    ("Animation", "src/Packages/Animation/EditorResources/Examples/AnimationGettingStarted.bpackage",
        "Animation.scene.yaml", "type: BEngine.Animation.Animator"),
    ("Animation", "src/Packages/Animation/EditorResources/Examples/StateMachine.bpackage",
        "StateMachine.scene.yaml", "type: BEngine.Animation.Animator"),
    ("Navigation2D", "src/Packages/Navigation2D/EditorResources/Examples/NavigationSurfaceAndAgent.bpackage",
        "Navigation.scene.yaml", "type: BEngine.Navigation2D.NavigationSurface2D"),
    ("Navigation2D", "src/Packages/Navigation2D/EditorResources/Examples/DynamicRebake.bpackage",
        "DynamicRebake.scene.yaml", "type: BEngine.Navigation2D.NavigationSurface2D"),
    ("Physics2D", "src/Packages/Physics2D/EditorResources/Examples/RigidbodyAndQueries.bpackage",
        "Physics2D.scene.yaml", "type: BEngine.Physics2D.Rigidbody2D"),
    ("Physics2D", "src/Packages/Physics2D/EditorResources/Examples/TriggersAndQueries.bpackage",
        "TriggersAndQueries.scene.yaml", "type: BEngine.Physics2D.BoxCollider2D"),
    ("PropertyAttributes",
        "src/Packages/PropertyAttributes/EditorResources/Examples/InspectorAttributesAndDrawer.bpackage",
        "PropertyAttributes.scene.yaml", "type: BEngine.Examples.PropertyAttributes"),
    ("PropertyAttributes", "src/Packages/PropertyAttributes/EditorResources/Examples/AttributesGallery.bpackage",
        "AttributesGallery.scene.yaml", "type: BEngine.Examples.PropertyAttributes"),
    ("TiledMap", "src/Packages/TiledMap/EditorResources/Examples/RuntimePainting.bpackage",
        "RuntimePainting.scene.yaml", "type: BEngine.TiledMap.Tilemap"),
    ("TiledMap", "src/Packages/TiledMap/EditorResources/Examples/AtlasPalette.bpackage",
        "AtlasPalette.scene.yaml", "type: BEngine.TiledMap.Tilemap"),
    ("UIElements", "src/Packages/UIElements/EditorResources/Examples/RuntimeHud.bpackage",
        "UIElements.scene.yaml", "type: BEngine.UIElements.UIDocument"),
    ("UIElements", "src/Packages/UIElements/EditorResources/Examples/ControlsGallery.bpackage",
        "ControlsGallery.scene.yaml", "type: BEngine.UIElements.UIDocument")
];

static bool IsPackageExampleEntry(string relativePath)
{
    var path = relativePath.Replace('\\', '/').Trim('/');
    var topLevel = path.Split('/', 2)[0];
    if (topLevel is "Editor" or "runtime" or "res") return true;
    return path.Equals("Editor.meta", StringComparison.Ordinal) ||
           path.Equals("runtime.meta", StringComparison.Ordinal) ||
           path.Equals("res.meta", StringComparison.Ordinal) ||
           path.Equals("Readme.md", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("Readme.md.meta", StringComparison.OrdinalIgnoreCase);
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
