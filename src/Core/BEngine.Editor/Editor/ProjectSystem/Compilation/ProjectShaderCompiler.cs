using System.Security.Cryptography;
using System.Text;
using BEngine.Editor;
using Veldrid;
using Veldrid.SPIRV;

namespace BEngine.ProjectSystem.Editor;

public static class ProjectShaderCompiler
{
    private const string Schema = "BEngine.ProjectShader.v1";

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
        Directory.CreateDirectory(workspace.ShaderArtifactsPath);

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
            var buildId = Path.GetFileNameWithoutExtension(artifact.Value);
            WritePointer(Path.Combine(Path.GetDirectoryName(artifact.Value)!, "current"), buildId);
        }
        foreach (var pointerPath in result.DeletedPointers.Values)
        {
            if (File.Exists(pointerPath)) File.Delete(pointerPath);
        }
        if (result.CompiledArtifacts.Count > 0 || result.DeletedAssets.Count > 0)
            BEngine.Rendering.Rhi.DefaultShaderResources.ClearCache();
    }

    public static string? ResolveCurrentArtifact(ProjectWorkspace workspace, string assetPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var normalized = NormalizeAssetPath(workspace, assetPath);
        var pointerPath = GetPointerPath(workspace, normalized);
        if (!File.Exists(pointerPath)) return null;
        var buildId = File.ReadAllText(pointerPath).Trim();
        if (buildId.Length == 0) return null;
        var artifact = Path.Combine(Path.GetDirectoryName(pointerPath)!, buildId + ".spv");
        return File.Exists(artifact) ? artifact : null;
    }

    internal static bool IsShaderPath(string path) =>
        path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase);

    private static string? CompileOne(
        ProjectWorkspace workspace,
        string assetPath,
        CancellationToken cancellationToken)
    {
        var sourcePath = workspace.ResolveInside(assetPath);
        var pointerPath = GetPointerPath(workspace, assetPath);
        if (!File.Exists(sourcePath)) return null;

        cancellationToken.ThrowIfCancellationRequested();
        var source = File.ReadAllText(sourcePath);
        var stage = ResolveStage(assetPath, source);
        var buildId = ComputeBuildId(assetPath, source, stage);
        var outputDirectory = Path.GetDirectoryName(pointerPath)!;
        var artifactPath = Path.Combine(outputDirectory, buildId + ".spv");
        Directory.CreateDirectory(outputDirectory);
        if (!File.Exists(artifactPath))
        {
            var result = SpirvCompilation.CompileGlslToSpirv(
                source, assetPath, stage, GlslCompileOptions.Default);
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryArtifact = artifactPath + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temporaryArtifact, result.SpirvBytes);
            File.Move(temporaryArtifact, artifactPath, true);
        }

        AppendLog(workspace, assetPath, $"Compiled {stage} -> {artifactPath}");
        return artifactPath;
    }

    private static void WritePointer(string pointerPath, string buildId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pointerPath)!);
        var temporaryPointer = pointerPath + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPointer, buildId, new UTF8Encoding(false));
        File.Move(temporaryPointer, pointerPath, true);
    }

    private static ShaderStages ResolveStage(string path, string source)
    {
        var name = path.ToLowerInvariant();
        if (name.EndsWith(".vert.glsl") || name.EndsWith(".vertex.glsl")) return ShaderStages.Vertex;
        if (name.EndsWith(".frag.glsl") || name.EndsWith(".fragment.glsl")) return ShaderStages.Fragment;
        if (name.EndsWith(".comp.glsl") || name.EndsWith(".compute.glsl")) return ShaderStages.Compute;

        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var value = line.Trim();
            const string pragma = "#pragma stage ";
            if (!value.StartsWith(pragma, StringComparison.OrdinalIgnoreCase)) continue;
            var stage = value[pragma.Length..].Trim();
            if (stage.Equals("vertex", StringComparison.OrdinalIgnoreCase)) return ShaderStages.Vertex;
            if (stage.Equals("fragment", StringComparison.OrdinalIgnoreCase) ||
                stage.Equals("pixel", StringComparison.OrdinalIgnoreCase)) return ShaderStages.Fragment;
            if (stage.Equals("compute", StringComparison.OrdinalIgnoreCase)) return ShaderStages.Compute;
            throw new InvalidDataException($"Unsupported shader stage '{stage.ToString()}'.");
        }

        throw new InvalidDataException(
            $"Shader '{path}' must use a stage suffix or declare '#pragma stage vertex|fragment|compute'.");
    }

    private static string ComputeBuildId(string assetPath, string source, ShaderStages stage)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Schema);
        Append(hash, assetPath.ToLowerInvariant());
        Append(hash, stage.ToString());
        hash.AppendData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string GetPointerPath(ProjectWorkspace workspace, string assetPath)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetPath.ToLowerInvariant())))
            .ToLowerInvariant();
        return Path.Combine(workspace.ShaderArtifactsPath, key, "current");
    }

    private static string NormalizeAssetPath(ProjectWorkspace workspace, string path)
    {
        var fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : workspace.ResolveInside(path);
        var assetsRoot = workspace.AssetsPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Shader path is outside Assets: {path}");
        return Path.GetRelativePath(workspace.RootPath, fullPath).Replace('\\', '/');
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void AppendLog(ProjectWorkspace workspace, string assetPath, string message)
    {
        var path = Path.Combine(EditorInstanceContext.current?.logsPath ?? workspace.LogsPath,
            "ShaderCompilation.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path,
            $"[{DateTimeOffset.Now:O}] {assetPath}{Environment.NewLine}{message}{Environment.NewLine}");
    }
}
