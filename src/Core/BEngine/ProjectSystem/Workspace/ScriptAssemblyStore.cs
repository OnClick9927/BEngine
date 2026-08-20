using System.Reflection;
using BEngine.Documents;

namespace BEngine.ProjectSystem;

public static class ScriptAssemblyStore
{
    private const string DocumentFormat = "BEngine.ScriptAssemblyReference";
    private const int DocumentVersion = 1;
    private const string ProjectManifestFormat = "BEngine.ProjectScriptAssemblies";
    private const int ProjectManifestVersion = 1;
    private const string ProjectManifestFileName = "ProjectAssemblies.yaml";

    public static string GetAssemblyRoot(ProjectWorkspace workspace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return GetAssemblyRoot(workspace.ScriptAssembliesPath, assemblyName);
    }

    public static string GetAssemblyRoot(string scriptAssembliesPath, string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ValidateAssemblyName(assemblyName);
        return Path.Combine(Path.GetFullPath(scriptAssembliesPath), assemblyName);
    }

    public static string GetReferencePath(ProjectWorkspace workspace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return GetReferencePath(workspace.ScriptAssembliesPath, assemblyName);
    }

    public static string GetReferencePath(string scriptAssembliesPath, string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ValidateAssemblyName(assemblyName);
        return Path.Combine(Path.GetFullPath(scriptAssembliesPath), $"{assemblyName}.current.yaml");
    }

    public static string GetProjectManifestPath(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return GetProjectManifestPath(workspace.ScriptAssembliesPath);
    }

    public static string GetProjectManifestPath(string scriptAssembliesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        return Path.Combine(Path.GetFullPath(scriptAssembliesPath), ProjectManifestFileName);
    }

    public static string? ResolveCurrentPath(ProjectWorkspace workspace, string assemblyName)
        => ResolveCurrentPath(workspace, assemblyName, workspace.ScriptAssembliesPath);

    public static string? ResolveCurrentPath(
        ProjectWorkspace workspace,
        string assemblyName,
        string scriptAssembliesPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        ValidateAssemblyName(assemblyName);
        var rootPath = Path.GetFullPath(scriptAssembliesPath);
        var referencePath = GetReferencePath(rootPath, assemblyName);
        if (File.Exists(referencePath))
        {
            var document = Document.Load<ScriptAssemblyReferenceDocument>(referencePath);
            ValidateDocument(document, assemblyName, referencePath);
            var resolved = ResolveInsideRoot(rootPath, document.RelativePath, referencePath);
            if (!File.Exists(resolved))
            {
                var legacy = Path.GetFullPath(Path.Combine(workspace.RootPath,
                    document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (legacy.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    resolved = legacy;
            }
            if (!File.Exists(resolved))
                throw new FileNotFoundException(
                    $"The current {assemblyName} script assembly does not exist.", resolved);
            return resolved;
        }

        // Compatibility with projects created before versioned script outputs were introduced.
        var legacyPath = Path.Combine(rootPath, $"{assemblyName}.dll");
        return File.Exists(legacyPath) ? Path.GetFullPath(legacyPath) : null;
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

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The compiled script assembly was not found.", fullPath);
        var rootPath = Path.GetFullPath(scriptAssembliesPath);
        EnsureInsideDirectory(rootPath, fullPath);
        var actualName = AssemblyName.GetAssemblyName(fullPath).Name;
        if (!string.Equals(actualName, assemblyName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Script assembly '{fullPath}' declares '{actualName}', expected '{assemblyName}'.");

        SaveAtomically(new ScriptAssemblyReferenceDocument
        {
            Assembly = assemblyName,
            BuildId = buildId,
            RelativePath = Path.GetRelativePath(rootPath, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
        }, GetReferencePath(rootPath, assemblyName));
    }

    public static ProjectScriptAssemblyManifestDocument? LoadProjectManifest(ProjectWorkspace workspace)
        => LoadProjectManifest(workspace, workspace.ScriptAssembliesPath);

    public static ProjectScriptAssemblyManifestDocument? LoadProjectManifest(
        ProjectWorkspace workspace,
        string scriptAssembliesPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptAssembliesPath);
        var rootPath = Path.GetFullPath(scriptAssembliesPath);
        var path = GetProjectManifestPath(rootPath);
        if (!File.Exists(path)) return null;
        var document = Document.Load<ProjectScriptAssemblyManifestDocument>(path);
        ValidateProjectManifest(document, rootPath, path);
        return document;
    }

    public static string ResolveProjectAssemblyPath(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyDocument assembly)
        => ResolveProjectAssemblyPath(workspace, assembly, workspace.ScriptAssembliesPath);

    public static string ResolveProjectAssemblyPath(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyDocument assembly,
        string scriptAssembliesPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assembly);
        var rootPath = Path.GetFullPath(scriptAssembliesPath);
        var manifestPath = GetProjectManifestPath(rootPath);
        ValidateAssemblyName(assembly.Assembly);
        var resolved = ResolveInsideRoot(rootPath, assembly.RelativePath, manifestPath);
        if (!File.Exists(resolved))
            throw new FileNotFoundException(
                $"Project script assembly '{assembly.Assembly}' does not exist.", resolved);
        var actualName = AssemblyName.GetAssemblyName(resolved).Name;
        if (!string.Equals(actualName, assembly.Assembly, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Project script assembly '{resolved}' declares '{actualName}', expected '{assembly.Assembly}'.");
        return resolved;
    }

    public static void PublishProjectManifest(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyManifestDocument manifest)
        => PublishProjectManifest(workspace, manifest, workspace.ScriptAssembliesPath);

    public static void PublishProjectManifest(
        ProjectWorkspace workspace,
        ProjectScriptAssemblyManifestDocument manifest,
        string scriptAssembliesPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(manifest);
        var rootPath = Path.GetFullPath(scriptAssembliesPath);
        Directory.CreateDirectory(rootPath);
        var path = GetProjectManifestPath(rootPath);
        ValidateProjectManifest(manifest, rootPath, path);
        SaveAtomically(manifest, path);
    }

    private static void SaveAtomically(Document document, string path)
    {
        var temporaryPath = $"{path}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            document.Save(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidateDocument(
        ScriptAssemblyReferenceDocument document,
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
        ProjectScriptAssemblyManifestDocument document,
        string rootPath,
        string manifestPath)
    {
        if (!string.Equals(document.Format, ProjectManifestFormat, StringComparison.Ordinal) ||
            document.Version != ProjectManifestVersion)
            throw new InvalidDataException(
                $"Project script assembly manifest '{manifestPath}' is invalid or unsupported.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in document.Assemblies)
        {
            ValidateAssemblyName(assembly.Assembly);
            if (!names.Add(assembly.Assembly))
                throw new InvalidDataException(
                    $"Project script assembly manifest '{manifestPath}' contains duplicate assembly " +
                    $"'{assembly.Assembly}'.");
            if (string.IsNullOrWhiteSpace(assembly.BuildId) || string.IsNullOrWhiteSpace(assembly.RelativePath))
                throw new InvalidDataException(
                    $"Project script assembly manifest '{manifestPath}' has an incomplete entry for " +
                    $"'{assembly.Assembly}'.");
            _ = ResolveInsideRoot(rootPath, assembly.RelativePath, manifestPath);
        }
    }

    private static string ResolveInsideRoot(
        string rootPath,
        string relativePath,
        string referencePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException(
                $"Script assembly reference '{referencePath}' must contain a relative path.");
        var resolved = Path.GetFullPath(Path.Combine(rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideDirectory(rootPath, resolved);
        return resolved;
    }

    private static void EnsureInsideDirectory(string rootPath, string path)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Script assembly path escapes '{Path.GetFullPath(rootPath)}': {path}");
    }

    private static void ValidateAssemblyName(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        if (assemblyName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            assemblyName.Contains(Path.DirectorySeparatorChar) ||
            assemblyName.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Assembly name contains invalid path characters.", nameof(assemblyName));
    }
}
