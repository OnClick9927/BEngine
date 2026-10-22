using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

public sealed record RuntimeManagedCodeReleaseInput(
    string Name,
    string BuildId,
    string AssemblyPath,
    string? SymbolsPath,
    string AssemblySha256,
    long AssemblySize,
    IReadOnlyList<string> Dependencies,
    string Origin);

public sealed record RuntimePackageResourceInput(
    string PackageId,
    string Address,
    string FilePath,
    string Sha256,
    long Size);

public sealed class RuntimeManagedCodeReleaseInputSet
{
    internal RuntimeManagedCodeReleaseInputSet(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies,
        IReadOnlyList<RuntimePackageResourceInput> packageResources,
        IReadOnlyList<string> packageResourceRoots)
    {
        Assemblies = assemblies;
        PackageResources = packageResources;
        PackageResourceRoots = packageResourceRoots;
    }

    public IReadOnlyList<RuntimeManagedCodeReleaseInput> Assemblies { get; }
    public IReadOnlyList<RuntimePackageResourceInput> PackageResources { get; }
    public IReadOnlyList<string> PackageResourceRoots { get; }
}

/// <summary>
/// Resolves the complete managed-code and package-resource input graph used by editor and Player releases.
/// </summary>
public static partial class RuntimeManagedCodeReleaseInputCollector
{
    private static FileUtility FileSystem => RuntimeServiceAccess.FileSystem;
    private static PathUtility Paths => RuntimeServiceAccess.PathService;

    public static RuntimeManagedCodeReleaseInputSet Collect(
        ProjectWorkspace workspace,
        string? scriptAssembliesPath = null) =>
        Collect(workspace, scriptAssembliesPath, FileSystem, Paths);

    public static RuntimeManagedCodeReleaseInputSet Collect(
        ProjectWorkspace workspace,
        string? scriptAssembliesPath,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var assemblyRoot = pathService.GetFullPath(
            scriptAssembliesPath ?? workspace.ScriptAssembliesPath);
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        AddProjectAssemblies(workspace, assemblyRoot, candidates, fileSystem, pathService);

        var packages = LoadEnabledPackages(workspace, fileSystem, pathService);
        AddPackageAssemblies(
            workspace, assemblyRoot, packages, candidates, fileSystem, pathService);
        AddExternalManagedDependencies(candidates, fileSystem, pathService);
        var assemblies = OrderAssemblies(candidates);
        var (resources, resourceRoots) = CollectResources(packages, fileSystem, pathService);
        return new RuntimeManagedCodeReleaseInputSet(assemblies, resources, resourceRoots);
    }

    private static void AddProjectAssemblies(
        ProjectWorkspace workspace,
        string assemblyRoot,
        IDictionary<string, Candidate> candidates,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var manifest = ScriptAssemblyStore.LoadProjectManifest(
            workspace, assemblyRoot, fileSystem, pathService);
        if (manifest is null)
        {
            var path = ScriptAssemblyStore.ResolveCurrentPath(
                workspace, "GameScripts", assemblyRoot, fileSystem, pathService);
            if (path is null) return;
            AddCandidate(candidates, CreateCandidate(
                "GameScripts", ComputeFileHash(path, fileSystem)[..24], path, [],
                "project:legacy", fileSystem, pathService));
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
                pathService));
        }
    }

    private static void AddPackageAssemblies(
        ProjectWorkspace workspace,
        string assemblyRoot,
        IReadOnlyList<EnabledPackage> packages,
        IDictionary<string, Candidate> candidates,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var packagesById = packages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
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

            var path = ScriptAssemblyStore.ResolveCurrentPath(
                           workspace, runtime.Assembly, assemblyRoot, fileSystem, pathService) ??
                       throw new FileNotFoundException(
                           $"Enabled package '{package.Id}' runtime assembly '{runtime.Assembly}' is missing.",
                           ScriptAssemblyStore.GetReferencePath(
                               assemblyRoot, runtime.Assembly, pathService));
            AddCandidate(candidates, CreateCandidate(
                runtime.Assembly,
                ResolveBuildId(
                    assemblyRoot, runtime.Assembly, path, fileSystem, pathService),
                path,
                dependencies,
                $"package:{package.Id}",
                fileSystem,
                pathService));
        }
    }

    private static Candidate CreateCandidate(
        string name,
        string buildId,
        string assemblyPath,
        IEnumerable<string> dependencies,
        string origin,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var paths = pathService;
        var path = paths.GetFullPath(assemblyPath);
        if (!fileSystem.FileExists(path))
            throw new FileNotFoundException($"Runtime assembly '{name}' from '{origin}' is missing.", path);
        var actualName = ReadAssemblyName(path, fileSystem);
        if (!name.Equals(actualName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Runtime assembly input '{path}' declares '{actualName}', expected '{name}' from '{origin}'.");
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
            ComputeFileHash(path, fileSystem),
            fileSystem.GetFileLength(path),
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
                Dependencies = (document.Dependencies ?? []).Select(packageId =>
                    new RuntimePackageDependency { PackageId = packageId }).ToList()
            };
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
        PathUtility pathService)
    {
        var referencePath = ScriptAssemblyStore.GetReferencePath(
            assemblyRoot, assemblyName, pathService);
        if (!fileSystem.FileExists(referencePath))
            return ComputeFileHash(assemblyPath, fileSystem)[..24];
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
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ValidatePackageId(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(character =>
                character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and
                not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
            throw new InvalidDataException($"Package id '{value}' in '{source}' is not a safe identifier.");
    }

    private static void ValidateContentDirectory(
        string value,
        string source,
        PathUtility pathService)
    {
        if (pathService.IsPathRooted(value) || value.Replace('\\', '/').Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidDataException(
                $"Package runtime content directory '{value}' in '{source}' must be a relative path.");
    }

    private static T LoadYaml<T>(
        string path,
        FileUtility fileSystem,
        PathUtility pathService) =>
        YamlUtility.Deserialize<T>(fileSystem.ReadAllText(pathService.GetFullPath(path)));

    private static string ReadAssemblyName(string path, FileUtility fileSystem)
    {
        try
        {
            using var stream = fileSystem.OpenRead(path);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
                throw new BadImageFormatException("The image does not contain managed metadata.", path);
            var metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
                throw new BadImageFormatException("The image is not a managed assembly.", path);
            return metadata.GetString(metadata.GetAssemblyDefinition().Name);
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw new BadImageFormatException($"Could not inspect managed assembly '{path}'.", exception);
        }
    }

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
        public List<RuntimePackageDependency> Dependencies { get; set; } = [];
    }

    private sealed class RuntimePackageDependency
    {
        public string PackageId { get; set; } = string.Empty;
        public string Target { get; set; } = "runtime";
        public bool Optional { get; set; }
    }
}
