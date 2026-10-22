using System.Text.RegularExpressions;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Serialization;
using Microsoft.CodeAnalysis.CSharp;

namespace BEngine.ProjectSystem;

/// <summary>Creates project extensions and exports validated source releases.</summary>
public static class BPackageAuthoring
{
    private static FileUtility Files => EditorHostServices.FileSystem;
    private static PathUtility Paths => EditorHostServices.Paths;

    public static string CreateTemplate(ProjectWorkspace workspace, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EditorAssetWritePolicy.EnsureCanWrite("Create package");
        var parent = Paths.Combine(workspace.AssetsPath, "Packages");
        RejectLinks(workspace.RootPath, parent);
        if (name is null)
        {
            name = "NewPackage";
            for (var index = 1; Files.DirectoryExists(Paths.Combine(parent, name)) || Files.FileExists(Paths.Combine(parent, name)); index++)
                name = "NewPackage" + index;
        }
        return CreateTemplate(workspace, new BPackageTemplateOptions
        {
            Name = name, Id = "com.project." + name.ToLowerInvariant().Replace('_', '-'), DisplayName = name,
            RootNamespace = "Project." + name
        });
    }

    public static string GetTemplatePath(ProjectWorkspace workspace, BPackageTemplateOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        var name = options.Name?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Package name must start with a letter and contain only letters, numbers and underscores.");
        ValidateId(options.Id?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(options.DisplayName)) throw new InvalidDataException("Display name is required.");
        var rootNamespace = options.RootNamespace?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(rootNamespace,
                @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Root namespace must contain dot-separated C# identifiers, for example Studio.Tools.");
        if (new[] { "Editor", "Resources", "BEngine", "BEngine.Editor" }.Contains(rootNamespace, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Root namespace conflicts with an engine assembly or a reserved package content directory.");
        BPackageVersion.Validate(options.PackageVersion?.Trim()!);
        BPackageVersion.Validate(options.MinimumEngineVersion?.Trim()!);
        var target = Paths.Combine(workspace.AssetsPath, "Packages", name);
        RejectLinks(workspace.RootPath, target);
        if (Files.DirectoryExists(target) || Files.FileExists(target)) throw new IOException($"Package already exists: {target}");
        return target;
    }

    public static string CreateTemplate(ProjectWorkspace workspace, BPackageTemplateOptions options)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Create package");
        var target = GetTemplatePath(workspace, options);
        var name = options.Name.Trim();
        var rootNamespace = options.RootNamespace.Trim();
        var assembly = rootNamespace;
        var sourceNamespace = string.Join(".", rootNamespace.Split('.').Select(part =>
            SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None ? part : "@" + part));
        var componentName = name + "ExampleComponent";
        var windowName = name + "Window";
        var document = new PackageDefinitionDocument
        {
            Id = options.Id.Trim(), DisplayName = options.DisplayName.Trim(),
            PackageVersion = options.PackageVersion.Trim(), MinimumEngineVersion = options.MinimumEngineVersion.Trim(),
            Description = options.Description?.Trim() ?? string.Empty,
            Runtime = new PackageAssemblyDocument { Assembly = assembly, RootNamespace = rootNamespace },
            Editor = new PackageAssemblyDocument
            {
                Assembly = assembly + ".Editor", RootNamespace = rootNamespace + ".Editor",
                Dependencies = [new PackageDependencyDocument { PackageId = options.Id.Trim(), Target = "runtime" }]
            }
        };
        var staging = Paths.Combine(workspace.TempPath, "package-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            Files.CreateDirectory(staging);
            document.Save(Paths.Combine(staging, "package.yaml"));
            foreach (var editor in new[] { false, true })
            {
                var current = editor ? assembly + ".Editor" : assembly;
                var directory = Paths.Combine(staging, current);
                Files.CreateDirectory(directory);
                new AssemblyDefinitionDocument
                {
                    Name = current, RootNamespace = editor ? rootNamespace + ".Editor" : rootNamespace, EditorOnly = editor,
                    References = editor ? [assembly] : []
                }.Save(Paths.Combine(directory, current + ".asmdef.yaml"));
                var sourceDirectory = editor ? directory : Paths.Combine(directory, "Runtime");
                Files.CreateDirectory(sourceDirectory);
                Files.WriteAllText(Paths.Combine(sourceDirectory, editor ? windowName + ".cs" : componentName + ".cs"), editor
                    ? $$"""
                      using BEngine.Editor;
                      namespace {{sourceNamespace}}.Editor;
                      public sealed class {{windowName}} : EditorWindow<{{windowName}}>
                      {
                          [MenuItem("Window/{{name}}")]
                          private static void Open() => GetWindow<{{windowName}}>().Show();
                          protected override void OnGUI() => GUILayout.Label("{{name}} is ready.");
                      }
                      """
                    : $$"""
                      using BEngine;
                      namespace {{sourceNamespace}};
                      [AddComponentMenu("{{name}}/Example Component")]
                      public sealed class {{componentName}} : MonoBehaviour { }
                      """);
            }
            Files.CreateDirectory(Paths.Combine(staging, "Resources"));
            Files.CreateDirectory(Paths.Combine(staging, "Editor"));
            Files.WriteAllText(Paths.Combine(staging, "Editor", "Readme.md"),
                $"# {name}\n\nReview package.yaml identity, packageVersion and minimumEngineVersion before exporting.\n" +
                "Keep assembly declarations and .asmdef.yaml references in sync. Runtime assets belong in Resources; editor assets in Editor.\n" +
                "Use Package Manager > Export Package to select this package from Assets/Packages, then validate and export to Packages-Exported next to Assets.\n");
            Files.CreateDirectory(Paths.GetDirectoryName(target)!);
            Files.MoveDirectory(staging, target);
            return target;
        }
        finally { if (Files.DirectoryExists(staging)) Files.DeleteDirectory(staging, true); }
    }

    public static string Publish(ProjectWorkspace workspace, string sourceDirectory, Action<int, int, string>? progress = null)
        => Export(workspace, sourceDirectory, BPackageRepository.rootPath, progress);

    public static string GetExportPath(ProjectWorkspace workspace, string sourceDirectory, string targetDirectory)
        => PrepareExport(workspace, sourceDirectory, targetDirectory).Destination;

    /// <summary>Lists package folders directly under the current project's Assets/Packages.</summary>
    public static string[] DiscoverProjectPackages(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var root = Paths.Combine(workspace.AssetsPath, "Packages");
        RejectLinks(workspace.RootPath, root);
        if (!Files.DirectoryExists(root)) return [];
        return Files.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => (Files.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            .Where(directory => Files.FileExists(Paths.Combine(directory, "package.yaml")) &&
                (Files.GetAttributes(Paths.Combine(directory, "package.yaml")) & FileAttributes.ReparsePoint) == 0)
            .Select(Paths.GetFullPath).OrderBy(Paths.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Previews Packages-Exported/name/name-version next to Assets without writing files.</summary>
    public static string GetProjectExportPath(ProjectWorkspace workspace, string sourceDirectory)
        => PrepareProjectExport(workspace, sourceDirectory).Destination;

    /// <summary>Exports an Assets/Packages package into Packages-Exported next to Assets.</summary>
    public static string ExportProjectPackage(ProjectWorkspace workspace, string sourceDirectory,
        Action<int, int, string>? progress = null)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Export package");
        return ExportPrepared(workspace, PrepareProjectExport(workspace, sourceDirectory), progress);
    }

    private static (string Source, string Destination, PackageDefinitionDocument Document) PrepareProjectExport(
        ProjectWorkspace workspace, string sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var source = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(Paths.IsPathRooted(sourceDirectory)
            ? sourceDirectory : Paths.Combine(workspace.RootPath, sourceDirectory)));
        var root = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(Paths.Combine(workspace.AssetsPath, "Packages")));
        if (!string.Equals(Paths.GetDirectoryName(source), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a package directly inside this project's Assets/Packages directory.");
        RejectLinks(workspace.RootPath, Paths.Combine(source, "package.yaml"));
        var target = Paths.Combine(Paths.GetDirectoryName(Paths.TrimEndingDirectorySeparator(workspace.AssetsPath))!, "Packages-Exported");
        return PrepareExport(workspace, source, target, preserveRepositoryFolder: false);
    }

    private static (string Source, string Destination, PackageDefinitionDocument Document) PrepareExport(
        ProjectWorkspace workspace, string sourceDirectory, string targetDirectory, bool preserveRepositoryFolder = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var source = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(Paths.IsPathRooted(sourceDirectory)
            ? sourceDirectory : Paths.Combine(workspace.RootPath, sourceDirectory)));
        RequireInside(workspace.RootPath, source);
        RejectLinks(workspace.RootPath, source);
        var manifestPath = Paths.Combine(source, "package.yaml");
        var authored = YamlUtility.Load<PackageDefinitionDocument>(manifestPath);
        if (string.IsNullOrWhiteSpace(authored.MinimumEngineVersion))
            throw new InvalidDataException("package.yaml must declare minimumEngineVersion before exporting.");
        var document = PackageDefinitionLoader.Normalize(authored, manifestPath);
        if (document.Type != "package") throw new InvalidDataException("Engine extension export requires type: package.");
        ValidateId(document.Id);
        if (!BPackageCompatibility.SupportsEngine(document, EngineVersion.Current))
            throw new InvalidDataException($"Export requires engine {document.MinimumEngineVersion} or newer for compilation validation.");
        var folder = Paths.GetFileName(source);
        ValidateFolder(folder);
        var repository = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(Paths.IsPathRooted(targetDirectory)
            ? targetDirectory : Paths.Combine(workspace.RootPath, targetDirectory)));
        if (IsInsideOrEqual(source, repository) || IsInsideOrEqual(repository, source))
            throw new InvalidDataException("Source and target directories must not overlap.");
        if (Files.FileExists(repository)) throw new IOException($"Target directory is a file: {repository}");
        RejectLinks(workspace.RootPath, repository);
        var releases = BPackageRepository.DiscoverDefinitionPaths(repository)
            .Select(path => new BPackageDefinition(path, PackageDefinitionLoader.Load(path))).ToArray();
        var existing = releases.FirstOrDefault(item => item.Document.Id.Equals(document.Id, StringComparison.OrdinalIgnoreCase));
        if (preserveRepositoryFolder && existing is not null)
        {
            var versionRoot = Paths.GetDirectoryName(existing.Path)!;
            var parent = Paths.GetDirectoryName(versionRoot)!;
            folder = Paths.GetFileName(Paths.GetFullPath(parent).Equals(Paths.GetFullPath(repository), StringComparison.OrdinalIgnoreCase)
                ? versionRoot : parent);
        }
        var packageRoot = Paths.Combine(repository, folder);
        var destination = Paths.Combine(packageRoot, $"{folder}-{document.PackageVersion}");
        RejectLinks(repository, destination);
        if (Files.DirectoryExists(destination) || Files.FileExists(destination) ||
            releases.Any(item => item.Document.Id.Equals(document.Id, StringComparison.OrdinalIgnoreCase) &&
                                 item.Document.PackageVersion == document.PackageVersion))
            throw new IOException($"Package version already exists. Increase packageVersion: {document.Id} {document.PackageVersion}");
        if (Files.DirectoryExists(packageRoot) && Files.EnumerateFiles(packageRoot, "package.yaml", SearchOption.AllDirectories)
                .Any(path => !PackageDefinitionLoader.Load(path).Id.Equals(document.Id, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"Repository folder belongs to another package: {packageRoot}");
        return (source, destination, document);
    }

    public static string Export(ProjectWorkspace workspace, string sourceDirectory, string targetDirectory,
        Action<int, int, string>? progress = null)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Export package");
        return ExportPrepared(workspace, PrepareExport(workspace, sourceDirectory, targetDirectory), progress);
    }

    private static string ExportPrepared(ProjectWorkspace workspace,
        (string Source, string Destination, PackageDefinitionDocument Document) prepared, Action<int, int, string>? progress)
    {
        var (source, destination, document) = prepared;
        var packageRoot = Paths.GetDirectoryName(destination)!;
        var available = BPackageCatalog.DiscoverAvailable();
        var validationRoot = Paths.Combine(workspace.TempPath, "PackagePublish", Guid.NewGuid().ToString("N"));
        var snapshot = Paths.Combine(validationRoot, "Source");
        Files.CreateDirectory(snapshot);
        CopySource(source, snapshot, document);
        // Compile the exact snapshot that is published, with independent build output and no project assembly references.
        var validation = new ProjectWorkspace(validationRoot, new ProjectData { Name = "Package validation" });
        var selected = new BPackageCatalog(available.packages.Where(item => !item.Document.Id.Equals(document.Id, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Path).Append(Paths.Combine(snapshot, "package.yaml")), validateGraph: false);
        selected.ValidateEnabled([document.Id]);
        var closure = selected.GetDependencyClosure(document.Id).Append(document.Id).Select(selected.GetRequired).ToArray();
        if (closure.Any(item => item.Document.Type != "package"))
            throw new InvalidDataException("Engine extensions cannot depend on editable project packages.");
        PackageSourceCompiler.CompileEnabled(validation, closure, ProcessUtility.Current, progress, isolated: true);
        EditorAssetWritePolicy.EnsureCanWrite("Export package");
        Files.CreateDirectory(packageRoot);
        var staging = Paths.Combine(packageRoot, ".publishing-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopySource(snapshot, staging, document);
            Files.MoveDirectory(staging, destination);
        }
        finally { if (Files.DirectoryExists(staging)) Files.DeleteDirectory(staging, true); }
        return destination;
    }

    private static void CopySource(string source, string destination, PackageDefinitionDocument document)
    {
        var assemblies = new[] { document.Runtime?.Assembly, document.Editor?.Assembly }.OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var assembly in assemblies) ValidateFolder(assembly);
        void CopyDirectory(string directory)
        {
            foreach (var entry in Files.EnumerateFileSystemEntries(directory))
            {
                var name = Paths.GetFileName(entry);
                if (name is "bin" or "obj" or ".git" or ".vs" or ".idea") continue;
                if ((Files.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Package cannot contain symbolic links: {entry}");
                var relative = Paths.GetRelativePath(source, entry);
                var parts = relative.Split('/', '\\');
                if (Files.DirectoryExists(entry))
                {
                    if (parts.Length == 1 && !assemblies.Contains(name) && name is not ("Resources" or "Editor"))
                        throw new InvalidDataException($"Unexpected package directory '{name}'. Declare assembly folders in package.yaml.");
                    CopyDirectory(entry);
                    continue;
                }
                if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Source packages cannot contain compiled assemblies: {entry}");
                if (parts.Length == 1 && name != "package.yaml" && !name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Move package content into Resources or Editor: {entry}");
                if (assemblies.Contains(parts[0]) && !name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Assembly folders may contain only source and assembly definitions: {entry}");
                if (assemblies.Contains(parts[0]) && name.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    var assemblyDefinition = YamlUtility.Load<AssemblyDefinitionDocument>(entry);
                    if (parts.Length != 2 || assemblyDefinition.Format != "BEngine.AssemblyDefinition" || assemblyDefinition.Version != 1)
                        throw new InvalidDataException($"Package assemblies require a single valid root assembly definition: {entry}");
                }
                var target = Paths.Combine(destination, relative);
                Files.CreateDirectory(Paths.GetDirectoryName(target)!);
                if (parts.Length == 1 && name == "package.yaml") document.Save(target);
                else Files.CopyFile(entry, target, false);
            }
        }
        CopyDirectory(source);
    }

    private static void ValidateFolder(string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant) || name.EndsWith('.'))
            throw new InvalidDataException($"Invalid package/assembly folder name '{name}'.");
    }

    private static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, @"^[a-z0-9]+(?:[.-][a-z0-9]+)+$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Package id must use a lowercase dotted identifier, for example com.studio.inventory.");
    }

    private static bool IsInsideOrEqual(string root, string path) =>
        root.Equals(path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.EndsWith(Paths.DirectorySeparatorChar) ? root : root + Paths.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static void RequireInside(string root, string path)
    {
        if (!path.StartsWith(Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(root)) + Paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package source must be a directory inside {root}.");
    }

    private static void RejectLinks(string root, string path)
    {
        for (string? current = path; current is not null; current = Paths.GetDirectoryName(current))
        {
            if ((Files.FileExists(current) || Files.DirectoryExists(current)) && (Files.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Package paths cannot traverse symbolic links: {current}");
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
}
