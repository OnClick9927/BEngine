using BEngine.ProjectSystem;
using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.Editor.Build.ProjectSystem;

/// <summary>
/// Resolves the complete managed-code and package-resource input graph used by editor and Player releases.
/// </summary>
public static partial class RuntimeManagedCodeReleaseInputCollector
{
    private static FileUtility FileSystem => RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
    private static PathUtility Paths => RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();

    public static RuntimeManagedCodeReleaseInputSet Collect(
        ProjectWorkspace workspace,
        string? scriptAssembliesPath = null) =>
        Collect(workspace, scriptAssembliesPath, FileSystem, Paths);

    public static RuntimeManagedCodeReleaseInputSet Collect(
        ProjectWorkspace workspace,
        string? scriptAssembliesPath,
        FileUtility fileSystem,
        PathUtility pathService) =>
        Collect(workspace, scriptAssembliesPath, scriptAssembliesPath, fileSystem, pathService);

    /// <summary>Collects project and package outputs from their explicitly selected roots.</summary>
    public static RuntimeManagedCodeReleaseInputSet Collect(
        ProjectWorkspace workspace,
        string? scriptAssembliesPath,
        string? packageAssembliesPath,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var assemblyRoot = pathService.GetFullPath(
            scriptAssembliesPath ?? workspace.ScriptAssembliesPath);
        var packageRoot = pathService.GetFullPath(
            packageAssembliesPath ?? workspace.ScriptAssembliesPath);
        var assemblyInputs = new AssemblyInputBatch(fileSystem, pathService);
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        AddProjectAssemblies(workspace, assemblyRoot, candidates, fileSystem, pathService, assemblyInputs);

        var packages = LoadEnabledPackages(workspace, fileSystem, pathService);
        AddPackageAssemblies(
            workspace, packageRoot, packages, candidates, fileSystem, pathService, assemblyInputs);
        AddExternalManagedDependencies(candidates, fileSystem, pathService, assemblyInputs);
        var assemblies = OrderAssemblies(candidates);
        var (resources, resourceRoots) = CollectResources(packages, fileSystem, pathService);
        return new RuntimeManagedCodeReleaseInputSet(assemblies, resources, resourceRoots);
    }

    private static void AddProjectAssemblies(
        ProjectWorkspace workspace,
        string assemblyRoot,
        IDictionary<string, Candidate> candidates,
        FileUtility fileSystem,
        PathUtility pathService,
        AssemblyInputBatch assemblyInputs)
    {
        var manifest = ScriptAssemblyStore.LoadProjectManifest(
            workspace, assemblyRoot, fileSystem, pathService);
        if (manifest is null)
        {
            var path = ScriptAssemblyStore.ResolveCurrentPath(
                workspace, "GameScripts", assemblyRoot, fileSystem, pathService);
            if (path is null) return;
            AddCandidate(candidates, CreateCandidate(
                "GameScripts", assemblyInputs.Read(path).Sha256[..24], path, [],
                "project:legacy", fileSystem, pathService, assemblyInputs));
            return;
        }

        foreach (var item in manifest.Assemblies)
        {
            var path = ScriptAssemblyStore.ResolveProjectAssemblyPath(
                workspace, item, assemblyRoot, fileSystem, pathService);
            AddCandidate(candidates, CreateCandidate(
                item.Assembly,
                item.BuildId,
                path,
                item.References,
                $"project:{item.Assembly}",
                fileSystem,
                pathService,
                assemblyInputs));
        }
    }

    private static void AddPackageAssemblies(
        ProjectWorkspace workspace,
        string assemblyRoot,
        IReadOnlyList<EnabledPackage> packages,
        IDictionary<string, Candidate> candidates,
        FileUtility fileSystem,
        PathUtility pathService,
        AssemblyInputBatch assemblyInputs)
    {
        var packagesById = BuildIndex5534(packages, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage> BuildIndex5534(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage> indexSource5534, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer5534)
        {
            ArgumentNullException.ThrowIfNull(indexSource5534, "source");
            var knownCount = indexSource5534.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage>(indexComparer5534);
            global::System.ReadOnlySpan<global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage> values;
            if (knownCount && indexSource5534 is global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage[] array)
                values = array;
            else if (knownCount && indexSource5534 is global::System.Collections.Generic.List<global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage>(capacity, indexComparer5534);
                foreach (var item in indexSource5534)
                    result.Add(IndexKey5534(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage>(values.Length, indexComparer5534);
                foreach (var item in values)
                    result.Add(IndexKey5534(item), item);
                return result;
            }
        
            string IndexKey5534(global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.EnabledPackage package) => package.Id;
        }
        foreach (var package in packages)
        {
            if (package.Runtime is not { } runtime) continue;
            var dependencies = new List<string>();
            foreach (var dependency in runtime.Dependencies)
            {
                if (!dependency.Target.Equals("runtime", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Package '{package.Id}' runtime assembly cannot depend on " +
                        $"'{dependency.PackageId}' target '{dependency.Target}'.");
                if (!packagesById.TryGetValue(dependency.PackageId, out var target))
                {
                    if (dependency.Optional) continue;
                    throw new InvalidDataException(
                        $"Enabled package '{package.Id}' depends on disabled or missing package " +
                        $"'{dependency.PackageId}'.");
                }
                if (target.Runtime is null)
                {
                    if (dependency.Optional) continue;
                    throw new InvalidDataException(
                        $"Enabled package '{package.Id}' depends on missing runtime assembly of " +
                        $"'{dependency.PackageId}'.");
                }
                dependencies.Add(target.Runtime.Assembly);
            }

            var path = runtime.Bootstrap
                ? pathService.Combine(pathService.GetDirectoryName(
                    typeof(RuntimeManagedCodeReleaseInputCollector).Assembly.Location)!, runtime.Assembly + ".dll")
                : ScriptAssemblyStore.ResolveCurrentPath(
                    workspace, runtime.Assembly, assemblyRoot, fileSystem, pathService);
            if (path is null || !fileSystem.FileExists(path))
                throw new FileNotFoundException(
                           $"Enabled package '{package.Id}' runtime assembly '{runtime.Assembly}' is missing.",
                           path ?? ScriptAssemblyStore.GetReferencePath(
                               assemblyRoot, runtime.Assembly, pathService));
            AddCandidate(candidates, CreateCandidate(
                runtime.Assembly,
                runtime.Bootstrap ? assemblyInputs.Read(path).Sha256[..24] : ResolveBuildId(
                    assemblyRoot, runtime.Assembly, path, fileSystem, pathService, assemblyInputs),
                path,
                dependencies,
                $"package:{package.Id}",
                fileSystem,
                pathService,
                assemblyInputs));
        }
    }

    private static Candidate CreateCandidate(
        string name,
        string buildId,
        string assemblyPath,
        IEnumerable<string> dependencies,
        string origin,
        FileUtility fileSystem,
        PathUtility pathService,
        AssemblyInputBatch assemblyInputs)
    {
        var paths = pathService;
        var path = paths.GetFullPath(assemblyPath);
        if (!fileSystem.FileExists(path))
            throw new FileNotFoundException($"Runtime assembly '{name}' from '{origin}' is missing.", path);
        var image = assemblyInputs.Read(path);
        if (!name.Equals(image.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Runtime assembly input '{path}' declares '{image.Name}', expected '{name}' from '{origin}'.");
        if (string.IsNullOrWhiteSpace(buildId))
            throw new InvalidDataException($"Runtime assembly '{name}' from '{origin}' has no build id.");
        var directory = paths.GetDirectoryName(path) ??
                        throw new InvalidDataException($"Runtime assembly '{path}' has no parent directory.");
        var symbolsPath = paths.Combine(directory, $"{paths.GetFileNameWithoutExtension(path)}.pdb");
        return new Candidate(
            name,
            buildId,
            path,
            fileSystem.FileExists(symbolsPath) ? symbolsPath : null,
            image.Sha256,
            image.Size,
            dependencies.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            origin);
    }

    private static void AddCandidate(IDictionary<string, Candidate> candidates, Candidate candidate)
    {
        if (candidates.TryGetValue(candidate.Name, out var existing))
            throw new InvalidDataException(
                $"Runtime assembly name '{candidate.Name}' is declared by both '{existing.Origin}' " +
                $"and '{candidate.Origin}'.");
        candidates.Add(candidate.Name, candidate);
    }

    private static IReadOnlyList<RuntimeManagedCodeReleaseInput> OrderAssemblies(
        IReadOnlyDictionary<string, Candidate> candidates)
    {
        var states = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<RuntimeManagedCodeReleaseInput>(candidates.Count);
        foreach (var name in candidates.Keys.OrderBy(value => value, StringComparer.Ordinal)) Visit(name);
        return ordered;

        void Visit(string name)
        {
            if (states.TryGetValue(name, out var state))
            {
                if (state == 2) return;
                if (state == 1)
                    throw new InvalidDataException($"Runtime managed-code dependency cycle at '{name}'.");
            }
            var candidate = candidates[name];
            states[name] = 1;
            foreach (var dependency in candidate.Dependencies)
                if (candidates.ContainsKey(dependency)) Visit(dependency);
            states[name] = 2;
            ordered.Add(new RuntimeManagedCodeReleaseInput(
                candidate.Name,
                candidate.BuildId,
                candidate.AssemblyPath,
                candidate.SymbolsPath,
                candidate.AssemblySha256,
                candidate.AssemblySize,
                candidate.Dependencies.Where(candidates.ContainsKey).ToArray(),
                candidate.Origin));
        }
    }

    private static IReadOnlyList<EnabledPackage> LoadEnabledPackages(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        if (!fileSystem.FileExists(workspace.PackageManifestPath)) return [];
        var manifest = LoadYaml<RuntimePackageManifest>(
            workspace.PackageManifestPath, fileSystem, pathService);
        if (!manifest.Format.Equals("BEngine.Packages", StringComparison.Ordinal) || manifest.Version != 1)
            throw new InvalidDataException(
                $"Package manifest '{workspace.PackageManifestPath}' is invalid or unsupported.");

        var manifestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in manifest.Packages)
        {
            ValidatePackageId(reference.Id, workspace.PackageManifestPath);
            if (!manifestIds.Add(reference.Id))
                throw new InvalidDataException(
                    $"Package manifest '{workspace.PackageManifestPath}' contains duplicate package " +
                    $"'{reference.Id}'.");
        }

        var definitions = new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);
        if (fileSystem.DirectoryExists(workspace.PackageCachePath))
        foreach (var path in fileSystem.EnumerateFiles(
                     workspace.PackageCachePath, "package.yaml", SearchOption.AllDirectories)
                 .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var document = LoadYaml<RuntimePackageDefinition>(path, fileSystem, pathService);
            var definition = NormalizeDefinition(document, path, pathService);
            if (!definitions.TryAdd(definition.Id, definition))
                throw new InvalidDataException(
                    $"Package '{definition.Id}' is defined more than once under '{workspace.PackageCachePath}'.");
        }

        var enabled = new List<EnabledPackage>();
        foreach (var reference in manifest.Packages.Where(item => item.Enabled && item.Type != "project")
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(reference.Id, out var definition))
                throw new FileNotFoundException(
                    $"Enabled package '{reference.Id}' has no package.yaml under '{workspace.PackageCachePath}'.");
            if (!string.IsNullOrWhiteSpace(reference.Version) &&
                !reference.Version.Equals(definition.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Enabled package '{reference.Id}' requests version '{reference.Version}', " +
                    $"but package.yaml declares '{definition.Version}'.");
            enabled.Add(new EnabledPackage(
                definition.Id,
                definition.RootPath,
                definition.RuntimeContentDirectory,
                definition.Runtime));
        }
        return enabled;
    }

    private static PackageDefinition NormalizeDefinition(
        RuntimePackageDefinition document,
        string path,
        PathUtility pathService)
    {
        if (!document.Format.Equals("BEngine.Package", StringComparison.Ordinal) || document.Version is not (1 or 2))
            throw new InvalidDataException($"Package definition '{path}' is invalid or unsupported.");
        ValidatePackageId(document.Id, path);
        RuntimePackageAssembly? runtime;
        if (document.Version == 1)
        {
            if (string.IsNullOrWhiteSpace(document.Assembly))
                throw new InvalidDataException($"Package definition '{path}' has no runtime assembly.");
            runtime = new RuntimePackageAssembly
            {
                Assembly = document.Assembly,
                Dependencies = ProjectItems15461()
            };
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.List<global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.RuntimePackageDependency> ProjectItems15461()
            {
                var projectionSource15461 = (document.Dependencies ?? []);
                ArgumentNullException.ThrowIfNull(projectionSource15461, "source");
                global::System.ReadOnlySpan<string> projectionValues15461 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource15461);
                var projectionOutput15461 = new global::System.Collections.Generic.List<global::BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInputCollector.RuntimePackageDependency>(projectionValues15461.Length);
                global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(projectionOutput15461, projectionValues15461.Length);
                var projectionTarget15461 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionOutput15461);
                var projectionIndex15461 = 0;
                foreach (var packageId in projectionValues15461)
                {
                    projectionTarget15461[projectionIndex15461++] = new RuntimePackageDependency
                    {
                        PackageId = packageId
                    };
                }
            
                return projectionOutput15461;
            }
        }
        else
            runtime = document.Runtime;

        if (runtime is not null)
        {
            if (string.IsNullOrWhiteSpace(runtime.Assembly))
                throw new InvalidDataException($"Package definition '{path}' has an empty runtime assembly name.");
            foreach (var dependency in runtime.Dependencies)
            {
                ValidatePackageId(dependency.PackageId, path);
                if (string.IsNullOrWhiteSpace(dependency.Target)) dependency.Target = "runtime";
            }
        }
        var runtimeContentDirectory = document.Content?.Runtime;
        if (string.IsNullOrWhiteSpace(runtimeContentDirectory)) runtimeContentDirectory = "Resources";
        ValidateContentDirectory(runtimeContentDirectory, path, pathService);
        return new PackageDefinition(
            document.Id,
            string.IsNullOrWhiteSpace(document.PackageVersion) ? "1.0.0" : document.PackageVersion,
            pathService.GetDirectoryName(pathService.GetFullPath(path))!,
            runtimeContentDirectory,
            runtime);
    }

    private static (IReadOnlyList<RuntimePackageResourceInput> Resources, IReadOnlyList<string> Roots)
        CollectResources(
        IEnumerable<EnabledPackage> packages,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var resources = new List<RuntimePackageResourceInput>();
        var roots = new List<string>();
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var paths = pathService;
            var resourceRoot = paths.Combine(
                package.RootPath,
                package.RuntimeContentDirectory.Replace('/', paths.DirectorySeparatorChar));
            if (!fileSystem.DirectoryExists(resourceRoot)) continue;
            var fullRoot = paths.TrimEndingDirectorySeparator(paths.GetFullPath(resourceRoot));
            roots.Add(fullRoot);
            foreach (var path in EnumerateRuntimeContentFiles(
                         fullRoot, fileSystem, pathService))
            {
                var relative = paths.GetRelativePath(fullRoot, path).Replace(paths.DirectorySeparatorChar, '/');
                if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                var address = $"Assets/Packages/{package.Id}/Resources/{relative}";
                if (!addresses.Add(address))
                    throw new InvalidDataException($"Duplicate package resource address '{address}'.");
                resources.Add(new RuntimePackageResourceInput(
                    package.Id,
                    address,
                    path,
                    ComputeFileHash(path, fileSystem),
                    fileSystem.GetFileLength(path)));
            }
        }
        return (resources, roots);
    }

    private static IReadOnlyList<string> EnumerateRuntimeContentFiles(
        string root,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var paths = pathService;
        if ((fileSystem.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Package runtime content root cannot be a reparse point: '{root}'.");
        var rootPrefix = root + paths.DirectorySeparatorChar;
        var pending = new Stack<string>();
        var files = new List<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            foreach (var child in fileSystem.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(value => value, StringComparer.Ordinal))
            {
                var fullPath = paths.GetFullPath(child);
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                    (fileSystem.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        $"Package runtime content directory escapes its root or is a reparse point: '{child}'.");
                pending.Push(fullPath);
            }
            foreach (var file in fileSystem.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var fullPath = paths.GetFullPath(file);
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                    (fileSystem.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        $"Package runtime content file escapes its root or is a reparse point: '{file}'.");
                files.Add(fullPath);
            }
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static string ResolveBuildId(
        string assemblyRoot,
        string assemblyName,
        string assemblyPath,
        FileUtility fileSystem,
        PathUtility pathService,
        AssemblyInputBatch assemblyInputs)
    {
        var referencePath = ScriptAssemblyStore.GetReferencePath(
            assemblyRoot, assemblyName, pathService);
        if (!fileSystem.FileExists(referencePath))
            return assemblyInputs.Read(assemblyPath).Sha256[..24];
        var reference = LoadYaml<ScriptAssemblyReferenceData>(
            referencePath, fileSystem, pathService);
        if (!reference.Format.Equals("BEngine.ScriptAssemblyReference", StringComparison.Ordinal) ||
            reference.Version != 1 ||
            !reference.Assembly.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(reference.BuildId))
            throw new InvalidDataException($"Script assembly reference '{referencePath}' is invalid.");
        return reference.BuildId;
    }

    private static string ComputeFileHash(string path, FileUtility fileSystem)
    {
        using var stream = fileSystem.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void ValidatePackageId(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || Matches22051())
            throw new InvalidDataException($"Package id '{value}' in '{source}' is not a safe identifier.");
        [global::BEngine.SkipLoopGuard]
        bool Matches22051()
        {
            var matchSource22051 = value;
            ArgumentNullException.ThrowIfNull(matchSource22051, "source");
            global::System.ReadOnlySpan<char> matchValues22051 = matchSource22051.AsSpan();
            foreach (var character in matchValues22051)
            {
                if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-')
                    return true;
            }
        
            return false;
        }
    }

    private static void ValidateContentDirectory(
        string value,
        string source,
        PathUtility pathService)
    {
        if (pathService.IsPathRooted(value) || value.AsSpan().HasInvalidPathSegment(whitespace: true))
            throw new InvalidDataException(
                $"Package runtime content directory '{value}' in '{source}' must be a relative path.");
    }

    private static T LoadYaml<T>(
        string path,
        FileUtility fileSystem,
        PathUtility pathService) =>
        YamlUtility.Deserialize<T>(fileSystem.ReadAllText(pathService.GetFullPath(path)));

    private sealed record Candidate(
        string Name,
        string BuildId,
        string AssemblyPath,
        string? SymbolsPath,
        string AssemblySha256,
        long AssemblySize,
        IReadOnlyList<string> Dependencies,
        string Origin);

    private sealed record EnabledPackage(
        string Id,
        string RootPath,
        string RuntimeContentDirectory,
        RuntimePackageAssembly? Runtime);

    private sealed record PackageDefinition(
        string Id,
        string Version,
        string RootPath,
        string RuntimeContentDirectory,
        RuntimePackageAssembly? Runtime);

    private sealed class RuntimePackageManifest
    {
        public string Format { get; set; } = "BEngine.Packages";
        public int Version { get; set; } = 1;
        public List<RuntimePackageReference> Packages { get; set; } = [];
    }

    private sealed class RuntimePackageReference
    {
        public string Type { get; set; } = "package";
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public bool Enabled { get; set; } = true;
    }

    private sealed class RuntimePackageDefinition
    {
        public string Format { get; set; } = "BEngine.Package";
        public int Version { get; set; } = 2;
        public string Id { get; set; } = string.Empty;
        public string PackageVersion { get; set; } = "1.0.0";
        public RuntimePackageContent? Content { get; set; }
        public RuntimePackageAssembly? Runtime { get; set; }
        public string? Assembly { get; set; }
        public List<string>? Dependencies { get; set; }
    }

    private sealed class RuntimePackageContent
    {
        public string Runtime { get; set; } = "Resources";
    }

    private sealed class RuntimePackageAssembly
    {
        public string Assembly { get; set; } = string.Empty;
        public bool Bootstrap { get; set; }
        public List<RuntimePackageDependency> Dependencies { get; set; } = [];
    }

    private sealed class RuntimePackageDependency
    {
        public string PackageId { get; set; } = string.Empty;
        public string Target { get; set; } = "runtime";
        public bool Optional { get; set; }
    }
}
