using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

// Pure document validation shared by the Editor and the standalone Hub.
public static class PackageDefinitionRules
{
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
            projectDirectory.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
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
                RootNamespace = string.IsNullOrWhiteSpace(document.Namespace) ? document.Assembly.Trim() : document.Namespace.Trim(),
                Dependencies = ProjectItems2437()
            };
            
            global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageDependencyDocument> ProjectItems2437()
            {
                var projectionSource2437 = (document.Dependencies ?? []);
                ArgumentNullException.ThrowIfNull(projectionSource2437, "source");
                global::System.ReadOnlySpan<string> projectionValues2437 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource2437);
                var projectionOutput2437 = new global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageDependencyDocument>(projectionValues2437.Length);
                global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(projectionOutput2437, projectionValues2437.Length);
                var projectionTarget2437 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionOutput2437);
                var projectionIndex2437 = 0;
                foreach (var packageId in projectionValues2437)
                {
                    projectionTarget2437[projectionIndex2437++] = new PackageDependencyDocument
                    {
                        PackageId = packageId,
                        Target = "runtime"
                    };
                }
            
                return projectionOutput2437;
            }
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
            !string.Equals(content.Editor, "Editor", StringComparison.Ordinal))
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
            Replaces = NormalizeAliases(document.Replaces, document.Id.Trim(), "replaces", sourceName),
            CommandLineOptions = ((((global::System.Collections.Generic.IEnumerable<string>)((document.CommandLineOptions ?? []).Select(option => option.Trim()))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
            {
                (string[] { Length: 0 }, _) => new global::System.Collections.Generic.List<string>(),
                var (distinctItems5713, distinctComparer5713) => distinctItems5713.ToHashSet<string>(distinctComparer5713).ToList()}),
            Content = new PackageContentDocument
            {
                Runtime = "Resources",
                Editor = "Editor"
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
                GitUrl = string.IsNullOrWhiteSpace(dependency.GitUrl) ? null : dependency.GitUrl.Trim(),
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
            Bootstrap = assembly.Bootstrap,
            Aliases = NormalizeAliases(assembly.Aliases, assembly.Assembly.Trim(), $"{kind}.aliases", sourceName),
            Dependencies = dependencies,
            HostReferences = ((((global::System.Collections.Generic.IEnumerable<string>)((assembly.HostReferences ?? []).Select(name => name.Trim()))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
            {
                (string[] { Length: 0 }, _) => new global::System.Collections.Generic.List<string>(),
                var (distinctItems9096, distinctComparer9096) => distinctItems9096.ToHashSet<string>(distinctComparer9096).ToList()}),
            PackageReferences = packageReferences
        };
    }

    private static List<string> NormalizeAliases(IEnumerable<string>? values, string owner,
        string field, string sourceName)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals(owner, StringComparison.OrdinalIgnoreCase))
                throw Invalid(sourceName, $"{field} contains an empty or self reference");
            var name = value.Trim();
            if (!seen.Add(name)) throw Invalid(sourceName, $"duplicate {field} entry '{name}'");
            result.Add(name);
        }
        return result;
    }

    private static InvalidDataException Invalid(string sourceName, string message) =>
        new($"Invalid package definition '{sourceName}': {message}.");
}
