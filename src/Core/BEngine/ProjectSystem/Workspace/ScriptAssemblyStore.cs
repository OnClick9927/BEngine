using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

public static class ScriptAssemblyStore
{
    private const string DocumentFormat = "BEngine.ScriptAssemblyReference";
    private const int DocumentVersion = 1;
    private const string ProjectManifestFormat = "BEngine.ProjectScriptAssemblies";
    private const int ProjectManifestVersion = 1;
    private const string ProjectManifestFileName = "ProjectAssemblies.yaml";

    private static FileUtility FileSystem => RuntimeServiceAccess.FileSystem;
    private static PathUtility Paths => RuntimeServiceAccess.PathService;
    private static PlatformUtility Platform => RuntimeServiceAccess.PlatformService;

    public static string GetAssemblyRoot(ProjectWorkspace workspace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return GetAssemblyRoot(workspace.ScriptAssembliesPath, assemblyName);
    }

    public static string GetAssemblyRoot(string scriptAssembliesPath, string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ValidateAssemblyName(assemblyName);
        return Paths.Combine(Paths.GetFullPath(scriptAssembliesPath), assemblyName);
    }

    public static string GetReferencePath(ProjectWorkspace workspace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return GetReferencePath(workspace.ScriptAssembliesPath, assemblyName);
    }

    public static string GetReferencePath(string scriptAssembliesPath, string assemblyName)
        => GetReferencePath(scriptAssembliesPath, assemblyName, Paths);

    public static string GetReferencePath(
        string scriptAssembliesPath,
        string assemblyName,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ArgumentNullException.ThrowIfNull(pathService);
        ValidateAssemblyName(assemblyName, pathService);
        return pathService.Combine(
            pathService.GetFullPath(scriptAssembliesPath), $"{assemblyName}.current.yaml");
    }

    public static string GetProjectManifestPath(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return GetProjectManifestPath(workspace.ScriptAssembliesPath);
    }

    public static string GetProjectManifestPath(string scriptAssembliesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        return Paths.Combine(Paths.GetFullPath(scriptAssembliesPath), ProjectManifestFileName);
    }

    public static string? ResolveCurrentPath(ProjectWorkspace workspace, string assemblyName)
        => ResolveCurrentPath(workspace, assemblyName, workspace.ScriptAssembliesPath);

    public static string? ResolveCurrentPath(
        ProjectWorkspace workspace,
        string assemblyName,
        string scriptAssembliesPath) =>
        ResolveCurrentPath(workspace, assemblyName, scriptAssembliesPath, FileSystem, Paths);

    public static string? ResolveCurrentPath(
        ProjectWorkspace workspace,
        string assemblyName,
        string scriptAssembliesPath,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ValidateAssemblyName(assemblyName, pathService);
        var paths = pathService;
        var rootPath = paths.GetFullPath(scriptAssembliesPath);
        var referencePath = GetReferencePath(rootPath, assemblyName, paths);
        if (fileSystem.FileExists(referencePath))
        {
            var document = LoadYaml<ScriptAssemblyReferenceData>(referencePath, fileSystem, paths);
            ValidateDocument(document, assemblyName, referencePath);
            var resolved = ResolveInsideRoot(rootPath, document.RelativePath, referencePath, paths);
            if (!fileSystem.FileExists(resolved))
            {
                var legacy = paths.GetFullPath(paths.Combine(workspace.RootPath,
                    document.RelativePath.Replace('/', paths.DirectorySeparatorChar)));
                if (legacy.StartsWith(rootPath + paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    resolved = legacy;
            }
            if (!fileSystem.FileExists(resolved))
                throw new FileNotFoundException(
                    $"The current {assemblyName} script assembly does not exist.", resolved);
            return resolved;
        }

        // Compatibility with projects created before versioned script outputs were introduced.
        var legacyPath = paths.Combine(rootPath, $"{assemblyName}.dll");
        return fileSystem.FileExists(legacyPath) ? paths.GetFullPath(legacyPath) : null;
    }

    public static void Publish(
        ProjectWorkspace workspace,
        string assemblyName,
        string buildId,
        string assemblyPath)
        => Publish(workspace, assemblyName, buildId, assemblyPath, workspace.ScriptAssembliesPath);

    public static void Publish(
        ProjectWorkspace workspace,
        string assemblyName,
        string buildId,
        string assemblyPath,
        string scriptAssembliesPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateAssemblyName(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        var paths = Paths;
        var fileSystem = FileSystem;
        var fullPath = paths.GetFullPath(assemblyPath);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("The compiled script assembly was not found.", fullPath);
        var rootPath = paths.GetFullPath(scriptAssembliesPath);
        EnsureInsideDirectory(rootPath, fullPath);
        var actualName = ReadAssemblyName(fullPath, fileSystem);
        if (!string.Equals(actualName, assemblyName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Script assembly '{fullPath}' declares '{actualName}', expected '{assemblyName}'.");

        SaveAtomically(new ScriptAssemblyReferenceData
        {
            Assembly = assemblyName,
            BuildId = buildId,
            RelativePath = paths.GetRelativePath(rootPath, fullPath)
                .Replace(paths.DirectorySeparatorChar, '/')
        }, GetReferencePath(rootPath, assemblyName));
    }

    public static ProjectScriptAssemblyManifestData? LoadProjectManifest(ProjectWorkspace workspace)
        => LoadProjectManifest(workspace, workspace.ScriptAssembliesPath);

    public static ProjectScriptAssemblyManifestData? LoadProjectManifest(
        ProjectWorkspace workspace,
        string scriptAssembliesPath) =>
        LoadProjectManifest(workspace, scriptAssembliesPath, FileSystem, Paths);

    public static ProjectScriptAssemblyManifestData? LoadProjectManifest(
        ProjectWorkspace workspace,
        string scriptAssembliesPath,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var rootPath = pathService.GetFullPath(scriptAssembliesPath);
        var path = pathService.Combine(rootPath, ProjectManifestFileName);
        if (!fileSystem.FileExists(path)) return null;
        var document = LoadYaml<ProjectScriptAssemblyManifestData>(path, fileSystem, pathService);
        ValidateProjectManifest(document, rootPath, path, pathService);
        return document;
    }

    public static string ResolveProjectAssemblyPath(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyData assembly)
        => ResolveProjectAssemblyPath(workspace, assembly, workspace.ScriptAssembliesPath);

    public static string ResolveProjectAssemblyPath(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyData assembly,
        string scriptAssembliesPath) =>
        ResolveProjectAssemblyPath(workspace, assembly, scriptAssembliesPath, FileSystem, Paths);

    public static string ResolveProjectAssemblyPath(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyData assembly,
        string scriptAssembliesPath,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var rootPath = pathService.GetFullPath(scriptAssembliesPath);
        var manifestPath = pathService.Combine(rootPath, ProjectManifestFileName);
        ValidateAssemblyName(assembly.Assembly, pathService);
        var resolved = ResolveInsideRoot(
            rootPath, assembly.RelativePath, manifestPath, pathService);
        if (!fileSystem.FileExists(resolved))
            throw new FileNotFoundException(
                $"Project script assembly '{assembly.Assembly}' does not exist.", resolved);
        var actualName = ReadAssemblyName(resolved, fileSystem);
        if (!string.Equals(actualName, assembly.Assembly, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Project script assembly '{resolved}' declares '{actualName}', expected '{assembly.Assembly}'.");
        return resolved;
    }

    public static void PublishProjectManifest(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyManifestData manifest)
        => PublishProjectManifest(workspace, manifest, workspace.ScriptAssembliesPath);

    public static void PublishProjectManifest(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyManifestData manifest,
        string scriptAssembliesPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(manifest);
        var rootPath = Paths.GetFullPath(scriptAssembliesPath);
        FileSystem.CreateDirectory(rootPath);
        var path = GetProjectManifestPath(rootPath);
        ValidateProjectManifest(manifest, rootPath, path);
        SaveAtomically(manifest, path);
    }

    private static void SaveAtomically<TData>(TData document, string path) where TData : class
    {
        var fileSystem = FileSystem;
        var temporaryPath = $"{path}.tmp.{Platform.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            YamlUtility.Save(document, temporaryPath);
            fileSystem.MoveFile(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (fileSystem.FileExists(temporaryPath)) fileSystem.DeleteFile(temporaryPath);
        }
    }

    private static void ValidateDocument(
        ScriptAssemblyReferenceData document,
        string assemblyName,
        string referencePath)
    {
        if (!string.Equals(document.Format, DocumentFormat, StringComparison.Ordinal) ||
            document.Version != DocumentVersion ||
            !string.Equals(document.Assembly, assemblyName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(document.RelativePath))
        {
            throw new InvalidDataException(
                $"Script assembly reference '{referencePath}' is invalid or unsupported.");
        }
    }

    private static void ValidateProjectManifest(
        ProjectScriptAssemblyManifestData document,
        string rootPath,
        string manifestPath) =>
        ValidateProjectManifest(document, rootPath, manifestPath, Paths);

    private static void ValidateProjectManifest(
        ProjectScriptAssemblyManifestData document,
        string rootPath,
        string manifestPath,
        PathUtility pathService)
    {
        if (!string.Equals(document.Format, ProjectManifestFormat, StringComparison.Ordinal) ||
            document.Version != ProjectManifestVersion)
            throw new InvalidDataException(
                $"Project script assembly manifest '{manifestPath}' is invalid or unsupported.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in document.Assemblies)
        {
            ValidateAssemblyName(assembly.Assembly, pathService);
            if (!names.Add(assembly.Assembly))
                throw new InvalidDataException(
                    $"Project script assembly manifest '{manifestPath}' contains duplicate assembly " +
                    $"'{assembly.Assembly}'.");
            if (string.IsNullOrWhiteSpace(assembly.BuildId) || string.IsNullOrWhiteSpace(assembly.RelativePath))
                throw new InvalidDataException(
                    $"Project script assembly manifest '{manifestPath}' has an incomplete entry for " +
                    $"'{assembly.Assembly}'.");
            _ = ResolveInsideRoot(rootPath, assembly.RelativePath, manifestPath, pathService);
        }
    }

    private static string ResolveInsideRoot(
        string rootPath,
        string relativePath,
        string referencePath) =>
        ResolveInsideRoot(rootPath, relativePath, referencePath, Paths);

    private static string ResolveInsideRoot(
        string rootPath,
        string relativePath,
        string referencePath,
        PathUtility pathService)
    {
        var paths = pathService;
        if (paths.IsPathRooted(relativePath))
            throw new InvalidDataException(
                $"Script assembly reference '{referencePath}' must contain a relative path.");
        var resolved = paths.GetFullPath(paths.Combine(rootPath,
            relativePath.Replace('/', paths.DirectorySeparatorChar)));
        EnsureInsideDirectory(rootPath, resolved, paths);
        return resolved;
    }

    private static void EnsureInsideDirectory(string rootPath, string path)
        => EnsureInsideDirectory(rootPath, path, Paths);

    private static void EnsureInsideDirectory(
        string rootPath,
        string path,
        PathUtility pathService)
    {
        var paths = pathService;
        var root = paths.GetFullPath(rootPath).TrimEnd(paths.DirectorySeparatorChar) +
                   paths.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Script assembly path escapes '{paths.GetFullPath(rootPath)}': {path}");
    }

    private static void ValidateAssemblyName(string assemblyName)
        => ValidateAssemblyName(assemblyName, Paths);

    private static void ValidateAssemblyName(string assemblyName, PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        var paths = pathService;
        if (assemblyName.IndexOfAny(paths.GetInvalidFileNameChars()) >= 0 ||
            assemblyName.Contains(paths.DirectorySeparatorChar) ||
            assemblyName.Contains(paths.AltDirectorySeparatorChar))
            throw new ArgumentException("Assembly name contains invalid path characters.", nameof(assemblyName));
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
}
