using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

public static class PackageDefinitionLoader
{
    public static PackageDefinitionDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = EditorHostServices.Paths.GetFullPath(path);
        return Normalize(YamlUtility.Load<PackageDefinitionDocument>(fullPath), fullPath);
    }

    public static PackageDefinitionDocument Normalize(
        PackageDefinitionDocument document,
        string sourceName = "package.yaml")
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document.Format, "BEngine.Package", StringComparison.Ordinal))
            throw Invalid(sourceName, $"unsupported format '{document.Format}'");
        if (document.Version is not (1 or 2))
            throw Invalid(sourceName, $"unsupported version {document.Version}");
        if (string.IsNullOrWhiteSpace(document.Id))
            throw Invalid(sourceName, "id is required");
        BPackageVersion.Validate(document.PackageVersion);
        // Old installed packages remain readable; all newly published packages require this field.
        var minimumEngineVersion = string.IsNullOrWhiteSpace(document.MinimumEngineVersion)
            ? "0.0.0" : document.MinimumEngineVersion.Trim();
        BPackageVersion.Validate(minimumEngineVersion);
        var type = document.Type?.Trim().ToLowerInvariant() ?? "package";
        if (type is not ("package" or "project"))
            throw Invalid(sourceName, "type must be package or project");
        var projectDirectory = document.ProjectDirectory?.Trim() ?? string.Empty;
        if (type == "project" && (projectDirectory.Length == 0 ||
            projectDirectory is "." or ".." || projectDirectory.EndsWith('.') ||
            projectDirectory.IndexOfAny(EditorHostServices.Paths.GetInvalidFileNameChars()) >= 0 ||
            projectDirectory.Contains('/') || projectDirectory.Contains('\\')))
            throw Invalid(sourceName, "projectDirectory must be a single folder name");

        var runtime = document.Runtime;
        var editor = document.Editor;
        if (document.Version == 1)
        {
            if (string.IsNullOrWhiteSpace(document.Assembly))
                throw Invalid(sourceName, "v1 assembly is required");
            runtime = new PackageAssemblyDocument
            {
                Assembly = document.Assembly.Trim(),
                RootNamespace = string.IsNullOrWhiteSpace(document.Namespace)
                    ? document.Assembly.Trim()
                    : document.Namespace.Trim(),
                Dependencies = (document.Dependencies ?? [])
                    .Select(packageId => new PackageDependencyDocument
                    {
                        PackageId = packageId,
                        Target = "runtime"
                    })
                    .ToList()
            };
        }
        else if (document.Assembly is not null || document.Namespace is not null ||
                 document.Dependencies is not null)
        {
            throw Invalid(sourceName, "v2 cannot mix legacy assembly, namespace or dependencies fields");
        }

        if (type == "package" && runtime is null && editor is null)
            throw Invalid(sourceName, "at least one runtime or editor assembly is required");

        var content = document.Content ?? new PackageContentDocument();
        if (!string.Equals(content.Runtime, "Resources", StringComparison.Ordinal) ||
            !string.Equals(content.Editor, EditorResource.DirectoryName, StringComparison.Ordinal))
            throw Invalid(sourceName, "content must map runtime to Resources and editor to Editor");

        runtime = NormalizeAssembly(runtime, "runtime", sourceName, allowEditorDependency: false);
        editor = NormalizeAssembly(editor, "editor", sourceName, allowEditorDependency: true);

        return new PackageDefinitionDocument
        {
            Format = "BEngine.Package",
            Version = 2,
            Type = type,
            ProjectDirectory = projectDirectory,
            Id = document.Id.Trim(),
            MinimumEngineVersion = minimumEngineVersion,
            PackageVersion = string.IsNullOrWhiteSpace(document.PackageVersion)
                ? "1.0.0"
                : document.PackageVersion.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(document.DisplayName)
                ? document.Id.Trim()
                : document.DisplayName.Trim(),
            Description = document.Description?.Trim() ?? string.Empty,
            EnabledByDefault = document.EnabledByDefault,
            Required = document.Required,
            CommandLineOptions = (document.CommandLineOptions ?? [])
                .Select(option => option.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Content = new PackageContentDocument
            {
                Runtime = "Resources",
                Editor = EditorResource.DirectoryName
            },
            Runtime = runtime,
            Editor = editor
        };
    }

    private static PackageAssemblyDocument? NormalizeAssembly(
        PackageAssemblyDocument? assembly,
        string kind,
        string sourceName,
        bool allowEditorDependency)
    {
        if (assembly is null) return null;
        if (string.IsNullOrWhiteSpace(assembly.Assembly))
            throw Invalid(sourceName, $"{kind}.assembly is required");
        if (string.IsNullOrWhiteSpace(assembly.RootNamespace))
            throw Invalid(sourceName, $"{kind}.rootNamespace is required");

        var dependencies = new List<PackageDependencyDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in assembly.Dependencies ?? [])
        {
            if (dependency is null || string.IsNullOrWhiteSpace(dependency.PackageId))
                throw Invalid(sourceName, $"{kind} dependency packageId is required");
            var target = dependency.Target?.Trim().ToLowerInvariant();
            if (target is not ("runtime" or "editor"))
                throw Invalid(sourceName,
                    $"{kind} dependency '{dependency.PackageId}' has invalid target '{dependency.Target}'");
            if (!allowEditorDependency && target == "editor")
                throw Invalid(sourceName,
                    $"runtime assembly cannot depend on editor assembly '{dependency.PackageId}'");

            var packageId = dependency.PackageId.Trim();
            if (!seen.Add($"{packageId}\0{target}"))
                throw Invalid(sourceName, $"duplicate {kind} dependency '{packageId}' ({target})");
            dependencies.Add(new PackageDependencyDocument
            {
                PackageId = packageId,
                Target = target,
                Optional = dependency.Optional
            });
        }

        var packageReferences = new List<PackageExternalReferenceDocument>();
        seen.Clear();
        foreach (var reference in assembly.PackageReferences ?? [])
        {
            if (reference is null || string.IsNullOrWhiteSpace(reference.Id))
                throw Invalid(sourceName, $"{kind} package reference id is required");
            if (string.IsNullOrWhiteSpace(reference.Version))
                throw Invalid(sourceName, $"{kind} package reference '{reference.Id}' version is required");
            var id = reference.Id.Trim();
            if (!seen.Add(id))
                throw Invalid(sourceName, $"duplicate {kind} package reference '{id}'");
            packageReferences.Add(new PackageExternalReferenceDocument
            {
                Id = id,
                Version = reference.Version.Trim()
            });
        }

        return new PackageAssemblyDocument
        {
            Assembly = assembly.Assembly.Trim(),
            RootNamespace = assembly.RootNamespace.Trim(),
            Dependencies = dependencies,
            HostReferences = (assembly.HostReferences ?? []).Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            PackageReferences = packageReferences
        };
    }

    private static InvalidDataException Invalid(string sourceName, string message) =>
        new($"Invalid package definition '{sourceName}': {message}.");
}
