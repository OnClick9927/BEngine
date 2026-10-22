using System.Security.Cryptography;
using System.Text;
using BEngine.Editor;

namespace BEngine.ProjectSystem.Editor;

public static class ProjectShaderCompiler
{
    private const string Schema = "BEngine.ProjectCgShader.v2";

    public static ShaderCompilationResult CompileChanged(
        ProjectWorkspace workspace,
        IEnumerable<string> assetPaths)
    {
        var result = CompileChangedInBackground(workspace, assetPaths);
        ApplyCompilationResult(result);
        return result;
    }

    internal static ShaderCompilationResult CompileChangedInBackground(
        ProjectWorkspace workspace,
        IEnumerable<string> assetPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetPaths);
        var compiled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deleted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EditorHostServices.FileSystem.CreateDirectory(workspace.ShaderArtifactsPath);

        foreach (var input in assetPaths.Where(IsShaderPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetPath = NormalizeAssetPath(workspace, input);
            try
            {
                var artifact = CompileOne(workspace, assetPath, cancellationToken);
                if (artifact is null) deleted[assetPath] = GetPointerPath(workspace, assetPath);
                else compiled[assetPath] = artifact;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors[assetPath] = exception.Message;
                AppendLog(workspace, assetPath, exception.ToString());
            }
        }

        return new ShaderCompilationResult(compiled, deleted, errors);
    }

    internal static void ApplyCompilationResult(ShaderCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var artifact in result.CompiledArtifacts)
        {
            var buildId = EditorHostServices.Paths.GetFileNameWithoutExtension(artifact.Value);
            WritePointer(EditorHostServices.Paths.Combine(EditorHostServices.Paths.GetDirectoryName(artifact.Value)!, "current"), buildId);
        }
        foreach (var pointerPath in result.DeletedPointers.Values)
        {
            if (EditorHostServices.FileSystem.FileExists(pointerPath)) EditorHostServices.FileSystem.DeleteFile(pointerPath);
        }
        if (result.CompiledArtifacts.Count > 0 || result.DeletedAssets.Count > 0)
            BEngine.Rendering.Rhi.DefaultShaderResources.ClearCache();
    }

    public static string? ResolveCurrentArtifact(ProjectWorkspace workspace, string assetPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var normalized = NormalizeAssetPath(workspace, assetPath);
        var pointerPath = GetPointerPath(workspace, normalized);
        if (!EditorHostServices.FileSystem.FileExists(pointerPath)) return null;
        var buildId = EditorHostServices.FileSystem.ReadAllText(pointerPath).Trim();
        if (buildId.Length == 0) return null;
        var artifact = EditorHostServices.Paths.Combine(EditorHostServices.Paths.GetDirectoryName(pointerPath)!, buildId + ".spv");
        return EditorHostServices.FileSystem.FileExists(artifact) ? artifact : null;
    }

    internal static bool IsShaderPath(string path) =>
        path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".cg", StringComparison.OrdinalIgnoreCase);

    private static string? CompileOne(
        ProjectWorkspace workspace,
        string assetPath,
        CancellationToken cancellationToken)
    {
        var sourcePath = workspace.ResolveInside(assetPath);
        var pointerPath = GetPointerPath(workspace, assetPath);
        if (!EditorHostServices.FileSystem.FileExists(sourcePath)) return null;

        cancellationToken.ThrowIfCancellationRequested();
        var source = EditorHostServices.FileSystem.ReadAllText(sourcePath);
        var stage = ResolveStage(assetPath, source);
        var entryPoint = ResolveEntryPoint(source);
        var settings = ReadImportSettings(workspace, assetPath, sourcePath);
        var compilerSource = RemoveEnginePragmas(source);
        var buildId = ComputeBuildId(assetPath, source, stage, entryPoint, settings);
        var outputDirectory = EditorHostServices.Paths.GetDirectoryName(pointerPath)!;
        var artifactPath = EditorHostServices.Paths.Combine(outputDirectory, buildId + ".spv");
        EditorHostServices.FileSystem.CreateDirectory(outputDirectory);
        if (!EditorHostServices.FileSystem.FileExists(artifactPath))
        {
            var bytes = ShaderCompilerUtility.Current.Compile(
                compilerSource,
                assetPath,
                stage,
                entryPoint,
                settings.StrictCompilation,
                settings.OptimizationLevel);
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryArtifact = artifactPath + $".{Guid.NewGuid():N}.tmp";
            EditorHostServices.FileSystem.WriteAllBytes(temporaryArtifact, bytes);
            EditorHostServices.FileSystem.MoveFile(temporaryArtifact, artifactPath, true);
        }

        AppendLog(workspace, assetPath, $"Compiled {stage} -> {artifactPath}");
        return artifactPath;
    }

    private static void WritePointer(string pointerPath, string buildId)
    {
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(pointerPath)!);
        var temporaryPointer = pointerPath + $".{Guid.NewGuid():N}.tmp";
        EditorHostServices.FileSystem.WriteAllText(temporaryPointer, buildId);
        EditorHostServices.FileSystem.MoveFile(temporaryPointer, pointerPath, true);
    }

    private static ShaderCompilationStage ResolveStage(string path, string source)
    {
        var name = path.ToLowerInvariant();
        if (name.EndsWith(".vert.cg") || name.EndsWith(".vertex.cg")) return ShaderCompilationStage.VertexShader;
        if (name.EndsWith(".frag.cg") || name.EndsWith(".fragment.cg")) return ShaderCompilationStage.FragmentShader;
        if (name.EndsWith(".comp.cg") || name.EndsWith(".compute.cg")) return ShaderCompilationStage.ComputeShader;

        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var value = line.Trim();
            const string pragma = "#pragma stage ";
            if (!value.StartsWith(pragma, StringComparison.OrdinalIgnoreCase)) continue;
            var stage = value[pragma.Length..].Trim();
            if (stage.Equals("vertex", StringComparison.OrdinalIgnoreCase)) return ShaderCompilationStage.VertexShader;
            if (stage.Equals("fragment", StringComparison.OrdinalIgnoreCase) ||
                stage.Equals("pixel", StringComparison.OrdinalIgnoreCase)) return ShaderCompilationStage.FragmentShader;
            if (stage.Equals("compute", StringComparison.OrdinalIgnoreCase)) return ShaderCompilationStage.ComputeShader;
            throw new InvalidDataException($"Unsupported shader stage '{stage.ToString()}'.");
        }

        throw new InvalidDataException(
            $"Shader '{path}' must use a stage suffix or declare '#pragma stage vertex|fragment|compute'.");
    }

    private static string ResolveEntryPoint(string source)
    {
        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var value = line.Trim();
            const string pragma = "#pragma entry ";
            if (!value.StartsWith(pragma, StringComparison.OrdinalIgnoreCase)) continue;
            var entryPoint = value[pragma.Length..].Trim().ToString();
            if (entryPoint.Length == 0) throw new InvalidDataException("CG shader entry point cannot be empty.");
            return entryPoint;
        }
        return "main";
    }

    private static string RemoveEnginePragmas(string source)
    {
        var builder = new StringBuilder(source.Length);
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.StartsWith("#pragma stage ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("#pragma entry ", StringComparison.OrdinalIgnoreCase)) continue;
            builder.AppendLine(line);
        }
        return builder.ToString();
    }

    private static ShaderImportSettings ReadImportSettings(
        ProjectWorkspace workspace,
        string assetPath,
        string sourcePath)
    {
        var metaPath = sourcePath + ".meta";
        if (!EditorHostServices.FileSystem.FileExists(metaPath)) return ShaderImportSettings.Default;
        try
        {
            var values = YamlUtility.Load<AssetMetaDocument>(metaPath).Settings ?? [];
            var strict = values.TryGetValue(nameof(ShaderImporter.strictCompilation), out var strictText) &&
                         bool.TryParse(strictText, out var strictValue)
                ? strictValue
                : ShaderImportSettings.Default.StrictCompilation;
            var optimization = values.TryGetValue(nameof(ShaderImporter.optimizationLevel), out var levelText) &&
                               int.TryParse(levelText,
                                   System.Globalization.NumberStyles.Integer,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out var levelValue)
                ? Math.Clamp(levelValue, 0, 3)
                : ShaderImportSettings.Default.OptimizationLevel;
            return new ShaderImportSettings(strict, optimization);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            AppendLog(workspace, assetPath,
                $"Could not read shader import settings: {exception.Message}");
            return ShaderImportSettings.Default;
        }
    }

    private static string ComputeBuildId(
        string assetPath,
        string source,
        ShaderCompilationStage stage,
        string entryPoint,
        ShaderImportSettings settings)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Schema);
        Append(hash, assetPath.ToLowerInvariant());
        Append(hash, stage.ToString());
        Append(hash, entryPoint);
        Append(hash, settings.StrictCompilation.ToString());
        Append(hash, settings.OptimizationLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AppendData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string GetPointerPath(ProjectWorkspace workspace, string assetPath)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetPath.ToLowerInvariant())))
            .ToLowerInvariant();
        return EditorHostServices.Paths.Combine(workspace.ShaderArtifactsPath, key, "current");
    }

    private static string NormalizeAssetPath(ProjectWorkspace workspace, string path)
    {
        var fullPath = EditorHostServices.Paths.IsPathRooted(path) ? EditorHostServices.Paths.GetFullPath(path) : workspace.ResolveInside(path);
        if (!workspace.AssetRoots.Any(root => fullPath.StartsWith(
                root.TrimEnd(EditorHostServices.Paths.DirectorySeparatorChar) + EditorHostServices.Paths.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Shader path is outside Assets: {path}");
        return EditorHostServices.Paths.GetRelativePath(workspace.RootPath, fullPath).Replace('\\', '/');
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void AppendLog(ProjectWorkspace workspace, string assetPath, string message)
    {
        var path = EditorHostServices.Paths.Combine(EditorInstanceContext.current?.logsPath ?? workspace.LogsPath,
            "ShaderCompilation.log");
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(path)!);
        EditorHostServices.AppendAllText(path,
            $"[{EditorHostServices.Platform.UtcNow.ToLocalTime():O}] {assetPath}" +
            $"{EditorHostServices.Platform.NewLine}{message}{EditorHostServices.Platform.NewLine}");
    }

    private readonly record struct ShaderImportSettings(bool StrictCompilation, int OptimizationLevel)
    {
        internal static ShaderImportSettings Default { get; } = new(true, 2);
    }
}
